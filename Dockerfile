FROM mcr.microsoft.com/dotnet/sdk:5.0 as build

COPY ./src /src
WORKDIR /src
RUN dotnet publish --configuration Release --runtime linux-x64 --output /build


FROM mcr.microsoft.com/dotnet/runtime:5.0

# http://www.hardkoded.com/blog/puppeteer-sharp-docker
RUN apt-get update && \
  apt-get -y install wget gnupg2 apt-utils && \
  wget -q -O - https://dl-ssl.google.com/linux/linux_signing_key.pub | apt-key add - && \
  sh -c 'echo "deb [arch=amd64] http://dl.google.com/linux/chrome/deb/ stable main" >> /etc/apt/sources.list.d/google.list' && \
  apt-get update && \
  apt-get install -y google-chrome-stable && \
  rm -rf /var/lib/apt/lists/* && \
  groupadd -r user && useradd -r -g user -G audio,video user && \
  mkdir -p /home/user/Downloads && \
  chown -R user:user /home/user
USER user
ENV PUPPETEER_EXECUTABLE_PATH "/usr/bin/google-chrome"

WORKDIR /app
COPY --from=build /build /app

ENTRYPOINT [ "./OnlinerByFlatBot" ]
