FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY BeautyBook.sln ./
COPY BeautyBookBackend/BeautyBookBackend.csproj BeautyBookBackend/
RUN dotnet restore BeautyBook.sln

COPY BeautyBookBackend/ BeautyBookBackend/
RUN dotnet publish BeautyBookBackend/BeautyBookBackend.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Production
ENTRYPOINT ["dotnet", "BeautyBookBackend.dll"]
