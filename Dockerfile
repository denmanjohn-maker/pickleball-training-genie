FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /src

COPY src/PickleballGenie.Api/PickleballGenie.Api.csproj PickleballGenie.Api/
COPY src/PickleballGenie.Data/PickleballGenie.Data.csproj PickleballGenie.Data/
COPY src/PickleballGenie.Models/PickleballGenie.Models.csproj PickleballGenie.Models/

RUN dotnet restore PickleballGenie.Api/PickleballGenie.Api.csproj

COPY src/ .
WORKDIR /src/PickleballGenie.Api
RUN dotnet publish -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "PickleballGenie.Api.dll"]
