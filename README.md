# TokyBay

> Search & Download audiobooks from Tokybook and convert it automatically to audiobook friendly M4B format.

## Contents

- [Download](#download)
- [Usage](#Usage)
- [Installation](#Installation)
- [Credits](#Credits)

## Download

Download the latest release at https://github.com/z00mable/TokyBay/releases/latest

## Usage

- Start TokyBay
  
    ```shell
    tokybay
    ```

- If TokyBay is invoked with `-d` or `--directory` as arguments it will change to a custom directory

    ```shell
    tokybay -d "C:\Users\User\Music"
    ```

> [!Tip]
>
> - If `-d` or `--directory` is not invoked, TokyBay will download the books to current directory. But directory can be change inside TokyBay's in-app settings.
>

### Features
1. **Search book**: Search and find audiobook from Tokybook.com
2. **Download from URL**: Directly download an audiobook using its URL.
3. **Convert to superior M4B format**: Convert downloaded MP3 files automatically to M4B audio book format.
4. **User settings**: Conveniently stored and easily changed in TokyBay's in-app settings.

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
- Keep or delete downloaded MP3 files (only possible when M4B conversion is activated).

> [!Note]
>
> - After activating M4b conversion, TokyBoy will automatically download [FFmpeg](https://github.com/FFmpeg/FFmpeg), if no FFmpeg directory is set in the in-app settings.
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

Inspired by projects from

- Adrian Castro https://github.com/castdrian/audiosnatch
- Rahatul Ghazi https://github.com/rahaaatul/TokySnatcher
- nazDridoy https://github.com/nazdridoy/audiobooksnatcher
