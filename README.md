# FlowMemo

[![License: GPL v2](https://img.shields.io/badge/License-GPL_v2-blue.svg?style=flat-square)](https://www.gnu.org/licenses/old-licenses/gpl-2.0.en.html)
[![Platform](https://img.shields.io/badge/Platform-Windows-0078D6?style=flat-square&logo=windows)](https://github.com/prorion/FlowMemo)

![FlowMemo 스크린샷](assets/Screenshot.png)


## 📢 소개

**FlowMemo**는 Windows용 포스트잇 메모 앱입니다. 메모를 다른 창 위에 고정(핀)해 두고, 우클릭 메뉴로 여러 텍스트 도구를 쓸 수 있습니다.


## 📜 원본 프로젝트와 라이선스

FlowMemo는 [63BeetleSmurf](https://github.com/63BeetleSmurf)의 [Pinny Notes](https://github.com/63BeetleSmurf/PinnyNotes) v1.17.0을 포크해 수정한 프로그램입니다.

- 원본과 같은 **GNU General Public License v2.0**을 따릅니다. 전문은 [LICENSE.txt](LICENSE.txt)에 있습니다.
- 원본에서 바뀐 내용과 날짜는 이 저장소의 커밋 기록과 [CHANGELOG.md](CHANGELOG.md)에 남깁니다.
- 원본 개발자를 후원하고 싶다면 [Pinny Notes 저장소](https://github.com/63BeetleSmurf/PinnyNotes)를 참고하세요.


## 🚀 주요 기능

- **핀 / 항상 위:** 메모를 다른 창보다 위에 띄워 둡니다.
- **자동 저장**
- **최소화 막기:** '바탕 화면 보기'를 눌러도 메모가 최소화되지 않게 할 수 있습니다.
- **색상·다크 모드·투명도**
- **시작 위치:** 새 메모가 화면 어디에 열릴지 정합니다.
- **복사/붙여넣기 옵션:** 공백 자동 제거, 가운데 클릭 붙여넣기, 선택 시 자동 복사, 선택이 없을 때 줄/전체 복사
- **선택:** 세 번 클릭으로 한 줄, 네 번 클릭으로 줄바꿈을 무시한 한 줄 선택
- **들여쓰기:** Tab으로 선택 영역 들여쓰기, 새 줄 자동 들여쓰기
- **맞춤법 검사:** Windows 맞춤법 검사기를 씁니다(지원하지 않는 언어에서는 자동으로 꺼집니다).
- **줄·단어·글자 수 보기**
- **트레이 아이콘:** 모든 메모를 앞으로 가져오거나 새 메모를 엽니다.
- **작업 표시줄 / Alt+Tab 표시 여부 설정**
- **텍스트 잠금:** 메모를 읽기 전용으로 바꿉니다.


## 🛠️ 텍스트 도구

Base64, 괄호, 대소문자, 색상(RGB↔HEX), 날짜·시간, 더미 텍스트, GUID, 해시(MD5/SHA), HTML 엔티티, 들여쓰기, 줄 합치기, JSON 정렬, 목록, 따옴표, 제거, 슬래시, 나누기, 공백 정리, URL 인코딩


## 🔨 빌드

필요한 것: Windows, [.NET 10 SDK](https://dotnet.microsoft.com/download)

```powershell
dotnet build FlowMemo.WpfUi -c Release
```

실행 파일은 `FlowMemo.WpfUi\bin\Release\net10.0-windows\FlowMemo.exe`에 생깁니다.

- **데이터 위치:** 기본은 `%APPDATA%\FlowMemo\flowmemo.sqlite`입니다.
- **포터블 모드:** `FlowMemo.exe` 옆에 빈 `portable.txt` 파일을 두면 DB를 exe 폴더에 만듭니다.
- **설치 파일(MSI):** `FlowMemo.Setup`은 Visual Studio와 *Microsoft Visual Studio Installer Projects* 확장이 있어야 빌드됩니다.

> WPF 기반이라 Windows에서만 동작합니다.
