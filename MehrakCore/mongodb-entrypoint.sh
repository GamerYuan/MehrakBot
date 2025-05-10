#!/bin/bash

# Start mongod in the background
echo "Starting mongod in background..."
mongod --bind_ip_all --fork --logpath /var/log/mongodb.log

# Wait until MongoDB is ready
echo "Waiting for MongoDB to start..."
until mongosh --eval "db.adminCommand('ping')" > /dev/null 2>&1; do
  sleep 1
done

echo "MongoDB is up. Starting GridFS initialization..."

DB_NAME=${MONGO_INITDB_DATABASE:-MehrakBot}

# Recursively find all files in /assets and upload them
find /assets -type f | while read -r filepath; do
  FILENAME=$(basename "$filepath")

  echo "Checking if $FILENAME exists in GridFS..."

  if ! mongofiles --db "$DB_NAME" --host localhost list | grep -q "$FILENAME"; then
    echo "Uploading $FILENAME to GridFS..."
    mongofiles --db "$DB_NAME" --host localhost put "$filepath"
  else
    echo "$FILENAME already exists. Skipping..."
  fi
done

# Keep mongod running in the foreground to keep the container alive
echo "Init complete. Bringing mongod to foreground..."
mongod --shutdown
mongod --bind_ip_all --logpath /var/log/mongodb.log
