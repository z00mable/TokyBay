# TokyBay

> Search & Download audiobooks from Tokybook

## Contents

- [Download](#download)
- [Usage](#Usage)
- [Installation](#Installation)

## Download

Download the latest release at https://github.com/z00mable/TokyBay/releases/latest

## Usage

- Invoke `-d` or `--directory` to add a custom directory

    ```shell
    tokybay -d "C:\Users\User\Music"
    ```

> [!NOTE]
>
> - If `-d` or `--directory` is not invoked, TokyBay will download the books in current directory.
>

### Features
1. **Search book**: Search and find audiobook from Tokybook.com
2. **Download from URL**: Directly download an audiobook using its URL.

### Search Functionality
- Enter your search query when prompted.
- Select the desired book from the search results.
- The program will automatically start downloading all audiobook chapters.

### Direct URL Download
- Enter the URL of the audiobook when prompted.
- The program will automatically start downloading the audiobook chapters.

> [!TIP]
> After downloading convert it into a proper m4b ebook with [AudioBookBinder](https://github.com/gonzoua/AudioBookBinder).

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
or

```sh
dotnet run --direcotry "C:\Users\User\Music"
```
