#!/bin/bash

git pull --autostash origin remod-core-impl
dotnet run -c Release
