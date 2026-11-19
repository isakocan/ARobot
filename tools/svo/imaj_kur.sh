#!/bin/bash
# WSL (Ubuntu-22.04, root) icinde calistir: SVO Pro imajini derler, gunlugu /root/svo_build.log'a yazar.
cd "$(dirname "$0")"
docker build -t svo_pro . > /root/svo_build.log 2>&1
echo "BITTI kod=$?" >> /root/svo_build.log
