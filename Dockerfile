# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project files first so the restore layer is cached until dependencies change.
COPY Brewline.sln ./
COPY src/Brewline.Web/Brewline.Web.csproj src/Brewline.Web/
COPY tests/Brewline.Tests/Brewline.Tests.csproj tests/Brewline.Tests/
RUN dotnet restore

COPY . .
RUN dotnet publish src/Brewline.Web -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# The database and the cookie-encryption keys live in a volume, so they survive container restarts.
RUN mkdir -p /data && chown -R app /data
VOLUME /data

# Payments default to the Demo provider. For real card payments set Payments__Provider=Stripe plus the two keys.
# The admin area stays switched off until Admin__Username and Admin__Password are set.
ENV ASPNETCORE_URLS=http://+:8080 \
    ConnectionStrings__Shop="Data Source=/data/brewline.db" \
    DataProtection__KeysPath=/data/keys \
    HttpsRedirection=false
EXPOSE 8080

# Never run a web server as root.
USER app
ENTRYPOINT ["dotnet", "Brewline.Web.dll"]
