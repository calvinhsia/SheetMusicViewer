FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

WORKDIR /src
COPY ["AvaloniaSimpleApp/AvaloniaSimpleApp.csproj", "AvaloniaSimpleApp/"]
RUN dotnet restore "AvaloniaSimpleApp/AvaloniaSimpleApp.csproj"

COPY . .
WORKDIR "/src/AvaloniaSimpleApp"
RUN dotnet build "AvaloniaSimpleApp.csproj" -c Release -o /app/build

FROM build AS test
RUN dotnet test "AvaloniaSimpleApp.csproj" -c Release --logger "trx;LogFileName=test_results.trx"

FROM build AS publish
RUN dotnet publish "AvaloniaSimpleApp.csproj" -c Release -o /app/publish

# Note: This is for build/test only. Avalonia UI apps need a display server to run.
