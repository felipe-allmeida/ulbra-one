.PHONY: run down

# Run the full local stack via .NET Aspire (API + Postgres + RabbitMQ + Azurite + web dashboard).
# Aspire orchestrates its OWN containers, so the docker-compose stack must be stopped first —
# otherwise they fight over ports (web 5173, api 5001). `docker compose down` keeps named volumes,
# but note Aspire uses its own ephemeral Postgres, so its DB starts fresh (the chart-of-accounts
# seed re-runs there; data in the compose Postgres is not shared).
run: down
	dotnet run --project src/Ulbra.One.AppHost

# Stop the docker-compose stack (containers only; volumes are preserved).
down:
	docker compose down
