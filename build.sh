#!/bin/bash
docker rmi -f idct-tech/rabbit-2-redis
docker build --progress=plain -t idct-tech/rabbit-2-redis .
docker tag idct-tech/rabbit-2-redis idct-tech/rabbit-2-redis:latest