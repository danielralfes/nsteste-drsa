# syntax=docker/dockerfile:1
#
# Dockerfile multi-stage da OrderService.Api.
#
# Local escolhido: ao lado do .csproj da API (padrão comum em soluções .NET
# com múltiplos projetos), mas o *build context* usado pelo docker-compose.yml
# é a raiz do repositório (`context: .`, `dockerfile: src/OrderService.Api/Dockerfile`)
# — necessário porque a API referencia os projetos Application/Infrastructure/
# Domain (ProjectReference), então o `dotnet restore`/`publish` precisa
# enxergar a solução inteira, não só a pasta da API.

# ---- Stage 1: build/publish -------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copia só os arquivos de projeto primeiro (melhor uso do cache de camadas do
# Docker: `restore` só reexecuta se algum .csproj/Directory.Build.props
# mudar, não a cada alteração de código-fonte). Directory.Build.props precisa
# vir antes do restore: é de lá que os .csproj herdam o TargetFramework.
COPY Directory.Build.props ./
COPY src/OrderService.Domain/OrderService.Domain.csproj src/OrderService.Domain/
COPY src/OrderService.Application/OrderService.Application.csproj src/OrderService.Application/
COPY src/OrderService.Infrastructure/OrderService.Infrastructure.csproj src/OrderService.Infrastructure/
COPY src/OrderService.Api/OrderService.Api.csproj src/OrderService.Api/
RUN dotnet restore src/OrderService.Api/OrderService.Api.csproj

# Agora copia o restante do código-fonte e publica.
COPY src/ src/
RUN dotnet publish src/OrderService.Api/OrderService.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish

# Gera o certificado de desenvolvimento HTTPS (`dotnet dev-certs`) durante o
# próprio build da imagem — `dotnet dev-certs` é uma ferramenta do SDK, não
# existe na imagem de runtime `aspnet:10.0` do stage `final`, por isso é
# gerado aqui e copiado como artefato pronto (`.pfx`) para o stage final, sem
# precisar instalar o SDK inteiro só para isso. A senha abaixo
# (`devcert-password`) não é um segredo: é só a senha local de um .pfx
# autoassinado e descartável, gerado do zero a cada build da imagem (ver
# docs/decisions.md seção 30).
RUN mkdir -p /https && dotnet dev-certs https -ep /https/aspnetapp.pfx -p devcert-password

# ---- Stage 2: runtime ---------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Porta HTTP customizada a pedido do usuário (faixa 5100), fora do padrão
# 8080 sugerido pelo .NET 8+ (docs/decisions.md seção 30). Porta HTTPS
# (5101) adicionada como conveniência de avaliação/execução local, usando um
# certificado de desenvolvimento gerado automaticamente durante o build desta
# imagem (stage `build` acima) — NÃO é uma configuração de produção (ver
# docs/decisions.md seção 30 e README).
ENV ASPNETCORE_HTTP_PORTS=5100
ENV ASPNETCORE_HTTPS_PORTS=5101
ENV ASPNETCORE_Kestrel__Certificates__Default__Path=/https/aspnetapp.pfx
ENV ASPNETCORE_Kestrel__Certificates__Default__Password=devcert-password
EXPOSE 5100 5101

# libgssapi-krb5-2: elimina o log de erro (`Cannot load library
# libgssapi_krb5.so.2`) que o Npgsql produz no primeiro connect de cada
# container novo, ao tentar negociação GSSAPI antes de cair para o fallback
# usuário/senha (docs/decisions.md seção 28.1). curl: usado pelo healthcheck
# do serviço `api` no docker-compose.yml contra o endpoint `/health`
# (docs/decisions.md seção 28.2).
RUN apt-get update && apt-get install -y --no-install-recommends libgssapi-krb5-2 curl && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .
COPY --from=build /https/aspnetapp.pfx /https/aspnetapp.pfx

ENTRYPOINT ["dotnet", "OrderService.Api.dll"]
