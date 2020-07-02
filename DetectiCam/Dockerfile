#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.


FROM mcr.microsoft.com/dotnet/core/runtime-deps:3.1.5-bionic AS base
#FROM mcr.microsoft.com/dotnet/core/runtime:3.1.5-bionic AS base
#FROM mcr.microsoft.com/dotnet/core/runtime:3.1-buster-slim AS base
WORKDIR /app
COPY ["CoreConsole/install-opencv-min-deps.sh", "/app"]
RUN /app/install-opencv-min-deps.sh

FROM base AS basedata
WORKDIR /data
COPY ["YoloData/*", "/yolo-data/"]

FROM mcr.microsoft.com/dotnet/core/sdk:3.1.301-bionic AS build
WORKDIR /src
COPY ["CoreConsole/CoreConsole.csproj", "CoreConsole/"]C:\Users\raimo\OneDrive\Repos\Cognitive-Samples-VideoFrameAnalysis\Windows\CoreConsole\Dockerfile
COPY ["VideoFrameAnalyzeCore/VideoFrameAnalyzeCore.csproj", "VideoFrameAnalyzeCore/"]
RUN dotnet restore "CoreConsole/CoreConsole.csproj" --runtime ubuntu.18.04-x64
COPY . .
WORKDIR "/src/CoreConsole"
RUN dotnet build "CoreConsole.csproj" -c Release --runtime ubuntu.18.04-x64 -o /app/build

FROM build AS publish
RUN dotnet publish "CoreConsole.csproj" -c Release --runtime ubuntu.18.04-x64 --self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=true -o /app/publish

FROM basedata AS final
VOLUME /captures
VOLUME /config
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["./CoreConsole", "--configdir=/config"]