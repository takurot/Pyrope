FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /app

# Copy csproj and restore as distinct layers
COPY src/Pyrope.GarnetServer/Pyrope.GarnetServer.csproj src/Pyrope.GarnetServer/
COPY src/Pyrope.GarnetServer/Extensions/VectorCommandSet.cs src/Pyrope.GarnetServer/Extensions/
# We need to copy files to restore? No, restore only needs csproj.
# But "COPY . ." later covers everything.

RUN dotnet restore src/Pyrope.GarnetServer/Pyrope.GarnetServer.csproj

# Copy everything else and build
COPY . .
WORKDIR /app/src/Pyrope.GarnetServer
RUN dotnet publish -c Release -o /app/out

# Build runtime image (ASP.NET Core)
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/out .

# Expose ports:
# - 3278: Garnet (RESP)
# - 5000: Control Plane (HTTP)
EXPOSE 3278
EXPOSE 5000

ENV ASPNETCORE_URLS=http://0.0.0.0:5000

# Start
ENTRYPOINT ["dotnet", "Pyrope.GarnetServer.dll", "--port", "3278", "--bind", "0.0.0.0"]
