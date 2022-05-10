#!/bin/bash

git pull --autostash origin main
dotnet run -c Release
