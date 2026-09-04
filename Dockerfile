# 阶段1：构建
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

# 复制所有文件
COPY . .

# 还原依赖并发布项目（生成自包含的 Linux 可执行文件）
RUN dotnet restore "DfoGmToolA21.sln" \
    && dotnet publish "DfoGmTool.csproj" -c Release -r linux-x64 --self-contained true -o /app/publish

# 阶段2：运行时
FROM debian:bookworm-slim AS final
WORKDIR /app

# 安装运行时依赖
RUN apt-get update && apt-get install -y libicu72 libssl3 && rm -rf /var/lib/apt/lists/*

# 从构建阶段复制发布结果
COPY --from=build /app/publish .

# 暴露 Web GM 工具的服务端口 (根据项目说明，默认是 5051)[reference:2]
EXPOSE 5051

# 设置容器启动命令
ENTRYPOINT ["./DfoGmTool"]
CMD ["--server-bin", "/app/data"]
