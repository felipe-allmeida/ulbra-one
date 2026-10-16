# Backplane de realtime: espalha sinais entre réplicas e guarda o estado de presença.
#
# Azure Managed Redis (Microsoft.Cache/redisEnterprise), não Azure Cache for Redis: o serviço antigo
# entrou em aposentadoria e o control plane passou a REJEITAR criação de instância nova com
# "Azure Cache for Redis is retiring, create Azure Managed Redis instance instead" (400).
# Ver https://aka.ms/AzureCacheForRedisRetirement.
#
# Menor tier em todos os ambientes — ver §11.1 do design. O teto que aperta primeiro é conexões
# simultâneas (uma assinatura por réplica), não memória: Balanced_B0 são 0,5 GB e 15.000 conexões.
resource "azurerm_managed_redis" "this" {
  name                = var.name
  location            = var.location
  resource_group_name = var.resource_group_name

  sku_name = var.sku_name

  # Réplica + SLA. Desligado só em dev/test, onde a queda do backplane custa presença, não dinheiro
  # (a Azure é explícita: "Only disable high availability for dev/test scenarios"). Mudar recria.
  high_availability_enabled = var.high_availability_enabled

  default_database {
    # NoCluster: o AMR é clusterizado por padrão (OSSCluster), e a própria Azure lista "Redis as a
    # message broker that doesn't require sharding" como caso de uso do não-clusterizado. É também o
    # que preserva a semântica de nó único que este backplane assume hoje. Custo: para crescer além
    # de 25 GB seria preciso trocar a política antes — irrelevante para um canal de sinais + dois
    # sorted sets de presença, e recriar este recurso não perde nada durável.
    clustering_policy = "NoCluster"

    # Chave de acesso em vez de Entra ID (§11.2 do design). O AMR SUPORTA Entra — ao contrário do SKU
    # Basic que motivou a decisão original —, mas usá-lo exige o pacote Microsoft.Azure.StackExchangeRedis
    # no app para renovar o token. Enquanto isso não existe, o default do provider (false) deixaria a
    # instância sem nenhuma autenticação utilizável pelo app.
    access_keys_authentication_enabled = true
  }

  tags = var.tags
}
