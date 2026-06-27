local typedefs = require "kong.db.schema.typedefs"

local plugin_name = "kong-opa-auth"

return {
  name = plugin_name,
  fields = {
    {
      config = {
        type = "record",
        fields = {
          {
            middleware_url = {
              type = "string",
              required = true,
              default = "http://localhost:5000/_auth",
              err = "must be a valid URL to the C# Auth Context Middleware",
            },
          },
          {
            opa_url = {
              type = "string",
              required = true,
              default = "http://localhost:8181/v1/data/authz/allow",
              err = "must be a valid URL to the Open Policy Agent evaluation endpoint",
            },
          },
          {
            gateway_client_cn = {
              type = "string",
              required = true,
              default = "api-gateway",
              err = "must specify the expected Common Name (CN) for the API Gateway mTLS certificate",
            },
          },
          {
            timeout = {
              type = "number",
              required = true,
              default = 5000,
              err = "must specify a timeout in milliseconds for HTTP subrequests",
            },
          },
        },
      },
    },
  },
}
