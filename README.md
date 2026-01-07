# TokyBay

Search & download audiobooks from [Tokybook.com](https://tokybook.com) and convert it automatically to audiobook friendly M4B format or good old MP3 format.
No installation is needed. Just download the latest release for your system at https://github.com/z00mable/TokyBay/releases/latest.

> [!Note]
> This project is intended for educational purposes only. Please respect copyright laws and the terms of service of the respective websites.

![Last Updated](https://img.shields.io/github/last-commit/z00mable/TokyBay?label=Last%20Updated)
![Repo Stars](https://img.shields.io/github/stars/z00mable/TokyBay?style=social)
![C#](https://img.shields.io/badge/C%23-239120?style=flat&logo=unity&logoColor=white)
![Platform](https://img.shields.io/badge/Platform-Cross--Platform-009688?logo=windows&logoColor=white)
![License](https://img.shields.io/github/license/z00mable/TokyBay?color=orange)
![PRs Welcome](https://img.shields.io/badge/PRs-welcome-brightgreen.svg)
![Issues](https://img.shields.io/github/issues/z00mable/TokyBay?color=informational)

## Supported Sites

* [tokybook.com](https://tokybook.com)

## Download

Download the latest release at https://github.com/z00mable/TokyBay/releases/latest

## Features
1. **Search book**: Search and find audiobook from Tokybook.com
2. **Download from URL**: Directly download an audiobook using its URL.
3. **Convert to superior M4B format**: Convert downloaded files automatically to M4B audio book format.
4. **Convert to MP3 format**: Convert downloaded files automatically to MP3 format.
5. **User settings**: Conveniently stored and easily changed in TokyBay's in-app settings.

### Search Functionality
- Enter your search query when prompted.
- Select the desired book from the search results.
- The program will automatically start downloading all audiobook chapters.

### Direct URL Download
- Enter the URL of the audiobook when prompted.
- The program will automatically start downloading the audiobook chapters.

### Settings
- Change download path.
- Activate/deactivate automatic M4B conversion after download.
- Activate/deactivate automatic MP3 conversion after download.

> [!Note]
>
> - On first start, TokyBoy will automatically download [FFmpeg](https://github.com/FFmpeg/FFmpeg) to current directory. Can be changed in in-app settings later.
>

## Usage

- Start TokyBay
  
    ```shell
    tokybay
    ```

- If TokyBay is invoked with `-d` or `--directory` as arguments it will download files to a custom directory

    ```shell
    tokybay -d "C:\Users\User\Music"
    ```

> [!Tip]
>
> - If `-d` or `--directory` is not invoked, TokyBay will download audiobooks to current directory. But directory can be change inside TokyBay's in-app settings.
>

## Installation
### Clone the repository
Open terminal and go to desired clone directory, then run:
```sh
git clone https://https://github.com/z00mable/TokyBay.git
cd TokyBay
```

### Install dependencies
```sh
dotnet restore
```

### Build the project
```sh
dotnet build
```

### Run the application
```sh
dotnet run -d "C:\Users\User\Music"
```

## License

FFmpeg codebase is mainly LGPL-licensed with optional components licensed under GPL. Please refer to the LICENSE file for detailed information.

Xabe.FFmpeg is licensed under [Attribution-NonCommercial-ShareAlike 3.0 Unported (CC BY-NC-SA 3.0)](https://creativecommons.org/licenses/by-nc-sa/3.0/) for non commercial use. If you want use Xabe.FFmpeg in commercial project visit our website - [Xabe.FFmpeg](https://ffmpeg.xabe.net/license.html)

## Credits

To [Tokybook.com](https://tokybook.com) for their awesomness.

Tokybay is inspired by projects from

- Adrian Castro https://github.com/castdrian/audiosnatch
- Rahatul Ghazi https://github.com/rahaaatul/TokySnatcher
- nazDridoy https://github.com/nazdridoy/audiobooksnatcher
