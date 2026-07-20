FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["TheLeague.Web/TheLeague.Web.csproj", "TheLeague.Web/"]
COPY ["TheLeague/TheLeague.csproj", "TheLeague/"]
COPY ["TheLeague.Mongo/TheLeague.Mongo.csproj", "TheLeague.Mongo/"]
RUN dotnet restore "TheLeague.Web/TheLeague.Web.csproj"
COPY . .
WORKDIR "/src/TheLeague.Web"
RUN dotnet publish "TheLeague.Web.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "TheLeague.Web.dll"]
