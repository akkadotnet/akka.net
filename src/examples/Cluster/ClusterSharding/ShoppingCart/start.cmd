dotnet publish -c Release 
docker build -t shopping-cart:latest .
docker-compose up
