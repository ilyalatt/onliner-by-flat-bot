#!/bin/bash
set -e

rm -rf build
dotnet publish --configuration Release --runtime linux-x64 --output build
dotnet restore # dotnet publish breaks dependencies
