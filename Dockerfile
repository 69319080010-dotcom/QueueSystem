FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY QueueSystem.csproj ./
RUN dotnet restore QueueSystem.csproj -p:QueueTargetFramework=net10.0
COPY . .
RUN dotnet publish QueueSystem.csproj -c Release -p:QueueTargetFramework=net10.0 --no-restore -o /publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /publish .
RUN install -d -o app -g app /app/data && touch /app/data/.keep
ENV ASPNETCORE_HTTP_PORTS=10000 \
    QueueDatabase__Path=/app/data/queues.db \
    TZ=Asia/Bangkok
EXPOSE 10000
USER app
ENTRYPOINT ["dotnet", "QueueSystem.dll"]
