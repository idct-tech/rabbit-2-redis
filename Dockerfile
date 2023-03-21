FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /app
COPY . .
RUN dotnet publish -c release -o /app

# final stage/image
FROM mcr.microsoft.com/dotnet/runtime:6.0
VOLUME [ "/var/log" ]
WORKDIR /app
COPY --from=build /app .
COPY appsettings.json appsettings.json
RUN ls -al /app
ENTRYPOINT ["dotnet", "Rabbit2Redis.dll"]