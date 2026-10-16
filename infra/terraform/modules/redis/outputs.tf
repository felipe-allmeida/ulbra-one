# Autenticação (§11.2 do design): esta é a única connection string do sistema que carrega uma senha
# em vez de depender de managed identity — ver o comentário em main.tf. O chamador (main.tf raiz)
# deve gravá-la no Key Vault e injetar via secret_env_vars — nunca como env_vars em texto plano.
#
# A porta vem do banco (10000 no AMR), não de um `ssl_port` do recurso: no Managed Redis a única
# porta é a TLS, e `client_protocol` default (`Encrypted`) já recusa tráfego em texto plano.
output "connection_string" {
  value = join(",", [
    "${azurerm_managed_redis.this.hostname}:${azurerm_managed_redis.this.default_database[0].port}",
    "password=${azurerm_managed_redis.this.default_database[0].primary_access_key}",
    "ssl=True",
    "abortConnect=False",
  ])
  sensitive = true
}
