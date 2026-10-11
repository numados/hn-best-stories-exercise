FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

COPY src/HackerNews.BestStories.Client/HackerNews.BestStories.Client.csproj src/HackerNews.BestStories.Client/
COPY src/HackerNews.BestStories.Api/HackerNews.BestStories.Api.csproj src/HackerNews.BestStories.Api/
RUN dotnet restore src/HackerNews.BestStories.Api/HackerNews.BestStories.Api.csproj

COPY src/HackerNews.BestStories.Client/ src/HackerNews.BestStories.Client/
COPY src/HackerNews.BestStories.Api/ src/HackerNews.BestStories.Api/
RUN dotnet publish src/HackerNews.BestStories.Api/HackerNews.BestStories.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app/publish .
USER app
ENTRYPOINT ["dotnet", "HackerNews.BestStories.Api.dll"]
