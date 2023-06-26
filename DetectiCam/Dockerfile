#FROM raimondb/opencv-dotnet-runtime-deps:0.23.0 AS base
FROM mcr.microsoft.com/dotnet/aspnet:6.0-focal AS base
RUN apt-get update && apt-get install -y \
    libgtk2.0-0 \
	  libtesseract4 \
	  libdc1394-22 \
	  libavformat57 \
	  libswscale4 \
	  libopenexr22 \
    curl \
    && rm -rf /var/lib/apt/lists/*
COPY --from=raimondb/yolov3-data ["yolov3.weights", "yolov3.cfg", "coco.names", "/yolo-data/"]
# RUN apt-get update && apt-get install -y \
#     curl \
#     && rm -rf /var/lib/apt/lists/*

FROM mcr.microsoft.com/dotnet/sdk:6.0-focal AS build
WORKDIR /src
COPY ["DetectiCam/DetectiCam.csproj", "DetectiCam/"]
COPY ["DetectiCam.Core/DetectiCam.Core.csproj", "DetectiCam.Core/"]
RUN dotnet restore "DetectiCam/DetectiCam.csproj" --runtime ubuntu.20.04-x64
COPY . .
WORKDIR "/src/DetectiCam"
RUN dotnet build "DetectiCam.csproj" -c Release --runtime ubuntu.20.04-x64 -o /app/build

FROM build AS publish
RUN dotnet publish "DetectiCam.csproj" -c Release --runtime ubuntu.20.04-x64 --self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=false -o /app/publish

FROM base AS final
VOLUME /captures
VOLUME /config
EXPOSE 80
ENV ASPNETCORE_URLS=http://+:80
WORKDIR /app
HEALTHCHECK --interval=30s --timeout=3s \
  CMD curl -f http://localhost/health || exit 1
COPY --from=publish /app/publish .
ENTRYPOINT ["./DetectiCam", "--configdir=/config"]
