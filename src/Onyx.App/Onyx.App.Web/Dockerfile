FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /app

COPY ./src/Onyx.Data/Onyx.Data.SQLite/Onyx.Data.SQLite.csproj ./src/Onyx.Data/Onyx.Data.SQLite/Onyx.Data.SQLite.csproj
RUN dotnet restore ./src/Onyx.Data/Onyx.Data.SQLite/Onyx.Data.SQLite.csproj
COPY ./src/Onyx.Data/Onyx.Data.SqlServer/Onyx.Data.SqlServer.csproj ./src/Onyx.Data/Onyx.Data.SqlServer/Onyx.Data.SqlServer.csproj
RUN dotnet restore ./src/Onyx.Data/Onyx.Data.SqlServer/Onyx.Data.SqlServer.csproj
COPY ./src/Onyx.Data/Onyx.Data.DataBaseSchema/Onyx.Data.DataBaseSchema.csproj ./src/Onyx.Data/Onyx.Data.DataBaseSchema/Onyx.Data.DataBaseSchema.csproj
RUN dotnet restore ./src/Onyx.Data/Onyx.Data.DataBaseSchema/Onyx.Data.DataBaseSchema.csproj
COPY ./src/Onyx.Data/Onyx.Data.ApiSchema/Onyx.Data.ApiSchema.csproj ./src/Onyx.Data/Onyx.Data.ApiSchema/Onyx.Data.ApiSchema.csproj
RUN dotnet restore ./src/Onyx.Data/Onyx.Data.ApiSchema/Onyx.Data.ApiSchema.csproj
COPY ./src/Onyx.App/Onyx.App.Shared/Onyx.App.Shared.csproj ./src/Onyx.App/Onyx.App.Shared/Onyx.App.Shared.csproj
RUN dotnet restore ./src/Onyx.App/Onyx.App.Shared/Onyx.App.Shared.csproj
COPY ./src/Onyx.App/Onyx.App.Web/Onyx.App.Web.csproj ./src/Onyx.App/Onyx.App.Web/Onyx.App.Web.csproj
RUN dotnet restore ./src/Onyx.App/Onyx.App.Web/Onyx.App.Web.csproj --no-cache

COPY ./src/Onyx.Data/Onyx.Data.SQLite/. ./src/Onyx.Data/Onyx.Data.SQLite/
COPY ./src/Onyx.Data/Onyx.Data.SqlServer/. ./src/Onyx.Data/Onyx.Data.SqlServer/
COPY ./src/Onyx.Data/Onyx.Data.DataBaseSchema/. ./src/Onyx.Data/Onyx.Data.DataBaseSchema/
COPY ./src/Onyx.Data/Onyx.Data.ApiSchema/. ./src/Onyx.Data/Onyx.Data.ApiSchema/
COPY ./src/Onyx.App/Onyx.App.Shared/. ./src/Onyx.App/Onyx.App.Shared/
COPY ./src/Onyx.App/Onyx.App.Web/. ./src/Onyx.App/Onyx.App.Web/

RUN dotnet publish -c Release -o /out ./src/Onyx.App/Onyx.App.Web/Onyx.App.Web.csproj

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /out ./

ENTRYPOINT ["dotnet", "Onyx.App.Web.dll"]
