FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /app

RUN --mount=type=cache,target=/var/cache/apt \
    --mount=type=cache,target=/var/lib/apt/lists \
    set -eux; \
    apt-get update; \
    apt-get -y install --no-install-recommends \
      apt-transport-https \
      software-properties-common \
      wget \
      unzip \
      ca-certificates \
      build-essential \
      cmake \
      git \
      ccache \
      libtbb-dev \
      libatlas-base-dev \
      libgtk-3-dev \
      libavcodec-dev \
      libavformat-dev \
      libswscale-dev \
      libdc1394-dev \
      libxine2-dev \
      libv4l-dev \
      libtheora-dev \
      libvorbis-dev \
      libxvidcore-dev \
      libopencore-amrnb-dev \
      libopencore-amrwb-dev \
      x264 \
      libtesseract-dev \
      libgdiplus \
      pkg-config \
      libavutil-dev; \
    apt-get -y clean; \
    rm -rf /var/lib/apt/lists/*

ARG OPENCV_VERSION

# Fetch OpenCV and opencv_contrib sources
RUN set -eux; \
    wget -q https://github.com/opencv/opencv/archive/${OPENCV_VERSION}.zip; \
    unzip -q ${OPENCV_VERSION}.zip; \
    rm ${OPENCV_VERSION}.zip; \
    mv opencv-${OPENCV_VERSION} /opencv; \
    wget -q https://github.com/opencv/opencv_contrib/archive/${OPENCV_VERSION}.zip; \
    unzip -q ${OPENCV_VERSION}.zip; \
    rm ${OPENCV_VERSION}.zip; \
    mv opencv_contrib-${OPENCV_VERSION} /opencv_contrib

# Configure, build, and install OpenCV with ccache and parallel build
RUN --mount=type=cache,target=/root/.ccache \
    set -eux; \
    cmake -S /opencv -B /opencv/build \
      -D OPENCV_EXTRA_MODULES_PATH=/opencv_contrib/modules \
      -D CMAKE_BUILD_TYPE=RELEASE \
      -D CMAKE_C_COMPILER_LAUNCHER=ccache \
      -D CMAKE_CXX_COMPILER_LAUNCHER=ccache \
      -D BUILD_SHARED_LIBS=OFF \
      -D ENABLE_CXX11=ON \
      -D BUILD_EXAMPLES=OFF \
      -D BUILD_DOCS=OFF \
      -D BUILD_PERF_TESTS=OFF \
      -D BUILD_TESTS=OFF \
      -D BUILD_JAVA=OFF \
      -D BUILD_opencv_app=OFF \
      -D BUILD_opencv_barcode=OFF \
      -D BUILD_opencv_java_bindings_generator=OFF \
      -D BUILD_opencv_js_bindings_generator=OFF \
      -D BUILD_opencv_python_bindings_generator=OFF \
      -D BUILD_opencv_python_tests=OFF \
      -D BUILD_opencv_ts=OFF \
      -D BUILD_opencv_js=OFF \
      -D BUILD_opencv_bioinspired=OFF \
      -D BUILD_opencv_ccalib=OFF \
      -D BUILD_opencv_datasets=OFF \
      -D BUILD_opencv_dnn_objdetect=OFF \
      -D BUILD_opencv_dpm=OFF \
      -D BUILD_opencv_fuzzy=OFF \
      -D BUILD_opencv_gapi=OFF \
      -D BUILD_opencv_intensity_transform=OFF \
      -D BUILD_opencv_mcc=OFF \
      -D BUILD_opencv_objc_bindings_generator=OFF \
      -D BUILD_opencv_rapid=OFF \
      -D BUILD_opencv_reg=OFF \
      -D BUILD_opencv_stereo=OFF \
      -D BUILD_opencv_structured_light=OFF \
      -D BUILD_opencv_surface_matching=OFF \
      -D BUILD_opencv_videostab=OFF \
      -D BUILD_opencv_wechat_qrcode=ON \
      -D WITH_GSTREAMER=OFF \
      -D WITH_ADE=OFF \
      -D OPENCV_ENABLE_NONFREE=ON; \
    cmake --build /opencv/build --parallel "$(nproc)"; \
    cmake --install /opencv/build; \
    ldconfig

# Get OpenCvSharp sources
RUN git clone --branch 4.11.0.20250507 https://github.com/shimat/opencvsharp.git /opencvsharp

# Build and install OpenCvSharp native extern with ccache
RUN --mount=type=cache,target=/root/.ccache \
    set -eux; \
    mkdir -p /opencvsharp/make; \
    cmake -S /opencvsharp/src -B /opencvsharp/make \
      -D CMAKE_INSTALL_PREFIX=/opencvsharp/make \
      -D CMAKE_C_COMPILER_LAUNCHER=ccache \
      -D CMAKE_CXX_COMPILER_LAUNCHER=ccache; \
    cmake --build /opencvsharp/make --parallel "$(nproc)"; \
    cmake --install /opencvsharp/make; \
    rm -rf /opencv /opencv_contrib; \
    cp /opencvsharp/make/OpenCvSharpExtern/libOpenCvSharpExtern.so /usr/lib/; \
    mkdir -p /artifacts; \
    cp /opencvsharp/make/OpenCvSharpExtern/libOpenCvSharpExtern.so /artifacts/

RUN apt update && apt install -y clang

COPY . .

ARG ASPNETCORE_ENVIRONMENT=Production
RUN dotnet restore --runtime linux-x64 Services/Application/Mehrak.Application/Mehrak.Application.csproj
RUN dotnet publish Services/Application/Mehrak.Application/Mehrak.Application.csproj \
    -c $([ "$ASPNETCORE_ENVIRONMENT" = "Development" ] && echo "Debug" || echo "Release" ) \
    -r linux-x64 -o /app/out

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS app
WORKDIR /app

COPY --from=build /usr/lib /usr/lib
COPY --from=build /artifacts /artifacts
COPY --from=build /app/out /app
ENTRYPOINT ["/app/Mehrak.Application"]
