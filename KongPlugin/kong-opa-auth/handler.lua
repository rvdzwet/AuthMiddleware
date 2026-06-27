local http = require "resty.http"
local cjson = require "cjson.safe"
local ngx = ngx
local kong = kong

local AuthHandler = {
  PRIORITY = 1000,
  VERSION = "1.0.0",
}

-- Helper to return a 403 Forbidden with an RFC 7807 ProblemDetails payload
local function return_forbidden(detail)
  return kong.response.exit(403, {
    status = 403,
    title = "Forbidden",
    detail = detail or "Access denied by Open Policy Agent."
  }, { ["Content-Type"] = "application/problem+json" })
end

-- Helper to make HTTP requests
local function make_http_request(url, method, headers, body, timeout)
  local httpc = http.new()
  httpc:set_timeout(timeout)
  
  local res, err = httpc:request_uri(url, {
    method = method,
    headers = headers,
    body = body,
    ssl_verify = false
  })
  
  return res, err
end

function AuthHandler:access(config)
  -- 1. East-West mTLS Bypass
  -- Read the client certificate's Subject DN provided by Nginx
  local client_dn = ngx.var.ssl_client_s_dn
  
  -- If there is a DN, check if it matches the configured gateway_client_cn
  if client_dn then
    -- Simple substring check for CN=... (A more robust check could parse the full DN)
    local cn_pattern = "CN=" .. config.gateway_client_cn
    if not string.find(client_dn, cn_pattern, 1, true) then
      kong.log.debug("mTLS Bypass: Client DN (", client_dn, ") is not the API Gateway. Passing East-West traffic.")
      return -- Bypass authorization completely!
    end
  else
    -- If there's no mTLS cert, we assume this is external/edge traffic reaching the gateway itself.
    kong.log.debug("No mTLS client certificate detected. Applying Edge Authorization.")
  end

  -- 2. Context Sub-Request to C# Middleware
  local original_path = kong.request.get_path()
  local original_method = kong.request.get_method()
  local request_headers = kong.request.get_headers()
  
  -- Prepare headers for the middleware
  local middleware_headers = {
    ["X-Forwarded-Path"] = original_path,
    ["X-Forwarded-Method"] = original_method,
    ["Content-Type"] = "application/json",
    ["Accept"] = "application/json"
  }
  
  -- Forward authorization headers to the middleware (e.g., Bearer tokens)
  if request_headers["authorization"] then
    middleware_headers["Authorization"] = request_headers["authorization"]
  end

  local mw_res, mw_err = make_http_request(config.middleware_url, "GET", middleware_headers, nil, config.timeout)
  
  if not mw_res then
    kong.log.err("Failed to call Auth Context Middleware: ", mw_err)
    return kong.response.exit(500, { error = "Internal Server Error during context generation" })
  end

  -- 3. Middleware Evaluation
  if mw_res.status ~= 200 then
    -- If the middleware returns 404 (Operation Not Found) or 403, immediately return it to the client.
    -- The middleware already formats these as RFC 7807 ProblemDetails.
    return kong.response.exit(mw_res.status, mw_res.body, { ["Content-Type"] = "application/problem+json" })
  end

  -- Middleware returned 200 OK. Parse the JSON context.
  local auth_context, parse_err = cjson.decode(mw_res.body)
  if not auth_context then
    kong.log.err("Failed to parse Auth Context Middleware JSON: ", parse_err)
    return kong.response.exit(500, { error = "Invalid JSON returned from context builder" })
  end

  -- 4. OPA Sub-Request
  local opa_payload = cjson.encode({ input = auth_context })
  local opa_headers = {
    ["Content-Type"] = "application/json",
    ["Accept"] = "application/json"
  }

  local opa_res, opa_err = make_http_request(config.opa_url, "POST", opa_headers, opa_payload, config.timeout)
  
  if not opa_res then
    kong.log.err("Failed to call OPA: ", opa_err)
    return kong.response.exit(500, { error = "Internal Server Error during policy evaluation" })
  end

  if opa_res.status ~= 200 then
    kong.log.err("OPA returned non-200 status: ", opa_res.status)
    return return_forbidden("Policy engine is unavailable or rejected the request format.")
  end

  local opa_body, opa_parse_err = cjson.decode(opa_res.body)
  if not opa_body or not opa_body.result then
    kong.log.err("Failed to parse OPA JSON or missing 'result' object: ", opa_parse_err)
    return return_forbidden("Policy engine returned an invalid response structure.")
  end

  -- 5. Enforcement & Downstream Header Injection
  -- Expecting `result.allow == true`
  if opa_body.result.allow ~= true then
    return return_forbidden()
  end

  -- Base64 encode the entire OPA `result` object for the downstream API
  local result_json = cjson.encode(opa_body.result)
  local result_b64 = ngx.encode_base64(result_json)
  
  -- Inject the header so the downstream API can read it
  kong.service.request.set_header("X-OPA-Result", result_b64)
end

return AuthHandler
