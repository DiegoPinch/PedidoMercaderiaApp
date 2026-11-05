
# ETAPA 1: Compilar frontend y backend
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build

WORKDIR /src

COPY . .

RUN dotnet publish Servidor/Servidor.csproj \
    -c Release \
    -o /app/publish \
    /p:UseAppHost=false

# ETAPA 2: Ejecutar el servidor
FROM mcr.microsoft.com/dotnet/aspnet:9.0

WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://0.0.0.0:10000

EXPOSE 10000

ENTRYPOINT ["dotnet", "Servidor.dll"]
