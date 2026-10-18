# Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Shop.slnx ./
COPY src/Shop.Core/Shop.Core.csproj src/Shop.Core/
COPY src/Shop.Infrastructure/Shop.Infrastructure.csproj src/Shop.Infrastructure/
COPY src/Shop.Web/Shop.Web.csproj src/Shop.Web/
RUN dotnet restore src/Shop.Web/Shop.Web.csproj
COPY src/ src/
RUN dotnet publish src/Shop.Web/Shop.Web.csproj -c Release -o /app --no-restore

# Run
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080 \
    DataProtection__KeysPath=/data/keys
VOLUME /data
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "Shop.Web.dll"]
