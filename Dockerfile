# Cross-compiles on the CI runner's native arch, produces amd64 + arm64 images
# (works on both Oracle Always Free shapes: AMD E2.1.Micro and Ampere A1).
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src
COPY src/PanProdanBot/PanProdanBot.csproj src/PanProdanBot/
RUN dotnet restore src/PanProdanBot/PanProdanBot.csproj -a $TARGETARCH
COPY src/ src/
RUN dotnet publish src/PanProdanBot/PanProdanBot.csproj -c Release -a $TARGETARCH --no-restore -o /app \
 && mkdir -p /data

FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /app .
COPY --from=build --chown=$APP_UID:$APP_UID /data /data
USER $APP_UID
ENV DOTNET_ENVIRONMENT=Production \
    Bot__DatabasePath=/data/bot.db
VOLUME /data
ENTRYPOINT ["dotnet", "PanProdanBot.dll"]
