
#!/usr/bin/env bash
set -euo pipefail

echo "Instalando SDK .NET 9..."

curl -fsSL https://dot.net/v1/dotnet-install.sh \
  -o /tmp/dotnet-install.sh

bash /tmp/dotnet-install.sh \
  --channel 9.0 \
  --install-dir "$HOME/.dotnet"

export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$DOTNET_ROOT:$PATH"

echo "Version instalada:"
dotnet --version

echo "Compilando PedidoMercaderia..."

dotnet publish "./PedidoMercaderia/PedidoMercaderia.csproj" \
  --configuration Release \
  --output "$PWD/dist" \
  -p:RunAOTCompilation=false

echo "Validando publicacion..."

test -f "$PWD/dist/wwwroot/index.html"

echo "Compilacion terminada correctamente."
