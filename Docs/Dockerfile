# 1. 빌드 스테이지: glibc 2.35를 사용하는 Ubuntu 22.04 베이스
FROM ubuntu:22.04 AS build

# 필수 의존성 설치 (clang 및 zlib은 Native AOT 필수)
RUN apt-get update && apt-get install -y \
    curl \
    git \
    clang \
    zlib1g-dev \
    libkrb5-dev \
    build-essential

# .NET 10 SDK 설치 (Microsoft 저장소 등록)
RUN curl -sSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin --channel 10.0 --install-dir /usr/share/dotnet
ENV PATH="$PATH:/usr/share/dotnet"
ENV DOTNET_ROOT="/usr/share/dotnet"

WORKDIR /src
COPY . .

# Native AOT 빌드 실행
# -r: 런타임 식별자 (linux-x64)
# -c: 구성 (Release)
RUN dotnet publish -r linux-x64 -c Release -o /app
