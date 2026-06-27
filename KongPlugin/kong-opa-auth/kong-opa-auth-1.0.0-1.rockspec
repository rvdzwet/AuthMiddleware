package = "kong-opa-auth"
version = "1.0.0-1"

source = {
  url = "git://github.com/your-org/AuthMiddleware.git",
  branch = "main",
  dir = "KongPlugin/kong-opa-auth"
}

description = {
  summary = "A Kong plugin that chains a C# Auth Context Builder with Open Policy Agent.",
  detailed = [[
    This plugin intercepts traffic, optionally bypasses mTLS East-West traffic, 
    fetches a JSON Auth Context from an external middleware, 
    evaluates it against OPA, and forwards the Base64-encoded OPA AST downstream.
  ]],
  homepage = "https://github.com/your-org/AuthMiddleware",
  license = "MIT"
}

dependencies = {
  "lua >= 5.1",
  "lua-resty-http >= 0.16.1"
}

build = {
  type = "builtin",
  modules = {
    ["kong.plugins.kong-opa-auth.handler"] = "handler.lua",
    ["kong.plugins.kong-opa-auth.schema"] = "schema.lua",
  }
}
