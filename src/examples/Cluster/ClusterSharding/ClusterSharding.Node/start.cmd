dotnet publish -c Release 
docker build -t cluster-sharding:latest .
docker-compose up
