#!/bin/bash
set -e

./build.sh

ep=worker@sns-index.com
bot_name=onliner-by-flat-bot
ssh $ep "cd $bot_name && cp -r bin bin-new"
rsync --recursive --delete --progress build/. $ep:~/$bot_name/bin-new
rm -rf build
ssh $ep "\
systemctl --user stop $bot_name && \
cd $bot_name && \
rm -rf bin && \
mv bin-new bin && \
systemctl --user start $bot_name"
