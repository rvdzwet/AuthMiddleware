package main

import (
	"encoding/base64"
	"encoding/json"
	"strings"

	"github.com/tetratelabs/proxy-wasm-go-sdk/proxywasm"
	"github.com/tetratelabs/proxy-wasm-go-sdk/proxywasm/types"
)

func main() {
	proxywasm.SetVMContext(&vmContext{})
}

type vmContext struct {
	types.DefaultVMContext
}

func (*vmContext) NewPluginContext(contextID uint32) types.PluginContext {
	return &pluginContext{}
}

type pluginContext struct {
	types.DefaultPluginContext
}

func (*pluginContext) NewHttpContext(contextID uint32) types.HttpContext {
	return &httpContext{}
}

// httpContext maintains the state machine across the async HTTP callbacks.
type httpContext struct {
	types.DefaultHttpContext
	// Original Request Data
	originalPath   string
	originalMethod string
	authorization  string
}

// 1. Intercept Request Headers
func (ctx *httpContext) OnHttpRequestHeaders(numHeaders int, endOfStream bool) types.Action {
	path, _ := proxywasm.GetHttpRequestHeader(":path")
	method, _ := proxywasm.GetHttpRequestHeader(":method")
	auth, _ := proxywasm.GetHttpRequestHeader("authorization")

	ctx.originalPath = path
	ctx.originalMethod = method
	ctx.authorization = auth

	// Bypass internal traffic if Envoy XFCC header is present and doesn't match API Gateway.
	// (Alternatively, rely on Kuma MeshTrafficPermissions natively).
	xfcc, err := proxywasm.GetHttpRequestHeader("x-forwarded-client-cert")
	if err == nil && !strings.Contains(xfcc, "api-gateway") {
		proxywasm.LogInfo("East-West Traffic detected via XFCC. Bypassing OPA.")
		return types.ActionContinue
	}

	headers := [][2]string{
		{":method", "GET"},
		{":path", "/_auth"},
		{":authority", "auth-middleware"}, // The Envoy cluster name for the C# SDK
		{"X-Forwarded-Path", ctx.originalPath},
		{"X-Forwarded-Method", ctx.originalMethod},
		{"Accept", "application/json"},
	}

	if ctx.authorization != "" {
		headers = append(headers, [2]string{"Authorization", ctx.authorization})
	}

	// Make asynchronous call to C# Auth Context Middleware
	// Note: "auth_middleware_cluster" must be a defined Envoy cluster in Kuma.
	if _, err := proxywasm.DispatchHttpCall("auth_middleware_cluster", headers, nil, nil, 5000, ctx.onAuthMiddlewareResponse); err != nil {
		proxywasm.LogCriticalf("Failed to dispatch to Auth Middleware: %v", err)
		return types.ActionContinue
	}

	// Pause the incoming HTTP request stream until callbacks complete.
	return types.ActionPause
}

// 2. Callback from C# Auth Middleware
func (ctx *httpContext) onAuthMiddlewareResponse(numHeaders, bodySize, numTrailers int) {
	status, _ := proxywasm.GetHttpCallResponseHeader(":status")
	
	// If C# returns 404/403, immediately return ProblemDetails to the client.
	if status != "200" {
		body, _ := proxywasm.GetHttpCallResponseBody(0, bodySize)
		proxywasm.SendHttpResponse(403, [][2]string{{"Content-Type", "application/problem+json"}}, body, -1)
		return
	}

	// Parse JSON context model from C# SDK
	authContextJson, err := proxywasm.GetHttpCallResponseBody(0, bodySize)
	if err != nil {
		proxywasm.SendHttpResponse(500, nil, []byte("Failed to read context body"), -1)
		return
	}

	// Prepare payload for OPA
	opaPayload := map[string]interface{}{
		"input": json.RawMessage(authContextJson),
	}
	opaBody, _ := json.Marshal(opaPayload)

	opaHeaders := [][2]string{
		{":method", "POST"},
		{":path", "/v1/data/authz/allow"},
		{":authority", "opa"},
		{"Content-Type", "application/json"},
	}

	// 3. Make asynchronous call to Open Policy Agent (OPA)
	if _, err := proxywasm.DispatchHttpCall("opa_cluster", opaHeaders, opaBody, nil, 5000, ctx.onOpaResponse); err != nil {
		proxywasm.LogCriticalf("Failed to dispatch to OPA: %v", err)
		proxywasm.ResumeHttpRequest()
	}
}

// 4. Callback from OPA and Enforcement
func (ctx *httpContext) onOpaResponse(numHeaders, bodySize, numTrailers int) {
	status, _ := proxywasm.GetHttpCallResponseHeader(":status")
	if status != "200" {
		proxywasm.SendHttpResponse(403, nil, []byte("Policy evaluation failed"), -1)
		return
	}

	opaResBody, _ := proxywasm.GetHttpCallResponseBody(0, bodySize)
	
	var opaResponse struct {
		Result struct {
			Allow bool `json:"allow"`
		} `json:"result"`
	}

	if err := json.Unmarshal(opaResBody, &opaResponse); err != nil {
		proxywasm.SendHttpResponse(500, nil, []byte("Failed to parse OPA response"), -1)
		return
	}

	// 5. Enforcement
	if !opaResponse.Result.Allow {
		proxywasm.SendHttpResponse(403, nil, []byte("Access denied by OPA"), -1)
		return
	}

	// Extract the full raw 'result' object to pass downstream
	var rawResponse map[string]interface{}
	json.Unmarshal(opaResBody, &rawResponse)
	
	resultBytes, _ := json.Marshal(rawResponse["result"])
	resultBase64 := base64.StdEncoding.EncodeToString(resultBytes)

	// Inject X-OPA-Result header for downstream C# APIs
	proxywasm.AddHttpRequestHeader("X-OPA-Result", resultBase64)

	// Authorization complete! Resume the paused Envoy request to the destination service.
	proxywasm.ResumeHttpRequest()
}
