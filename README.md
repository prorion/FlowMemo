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
- FlowMemo의 버전은 원본(1.x)과 구분하려고 **2.0.0**부터 시작합니다.
- 원본 개발자를 후원하고 싶다면 [Pinny Notes 저장소](https://github.com/63BeetleSmurf/PinnyNotes)를 참고하세요.


## 🚀 주요 기능

- **핀 / 항상 위:** 메모를 다른 창보다 위에 띄워 둡니다.
- **자동 저장**
- **자동 백업과 휴지통:** 하루 한 번 DB를 백업하고(최근 7일), 지운 메모는 휴지통에 30일 보관합니다.
- **최소화 막기:** '바탕 화면 보기'를 눌러도 메모가 최소화되지 않게 할 수 있습니다.
- **색상·다크 모드·투명도**
- **시작 위치:** 새 메모가 화면 어디에 열릴지 정합니다.
- **복사/붙여넣기 옵션:** 공백 자동 제거, 가운데 클릭 붙여넣기, 선택 시 자동 복사, 선택이 없을 때 줄/전체 복사
- **선택:** 세 번 클릭으로 한 줄, 네 번 클릭으로 줄바꿈을 무시한 한 줄 선택
- **들여쓰기:** Tab으로 선택 영역 들여쓰기, 새 줄 자동 들여쓰기
- **맞춤법 검사:** Windows 맞춤법 검사기를 씁니다(지원하지 않는 언어에서는 자동으로 꺼집니다).
- **줄·단어·글자 수 보기**
- **트레이 아이콘:** 모든 메모를 앞으로 가져오거나 새 메모를 엽니다.
- **트레이 전용 실행:** 메모·관리·설정 창을 작업 표시줄에 표시하지 않고 트레이로만 관리합니다(기본값, 설정에서 끌 수 있습니다).
- **모든 메모 보기/숨기기:** 트레이 메뉴나 전역 단축키(기본 Ctrl+Alt+H, 설정에서 변경)로 한 번에 숨기고 다시 띄웁니다.
- **Windows 시작할 때 자동 실행**
- **작업 표시줄 / Alt+Tab 표시 여부 설정**
- **텍스트 잠금:** 메모를 읽기 전용으로 바꿉니다. 다시 실행해도 유지됩니다.


## 🛠️ 텍스트 도구

Base64, 괄호, 대소문자, 색상(RGB↔HEX), 날짜·시간, 더미 텍스트, GUID, 해시(MD5/SHA), HTML 엔티티, 들여쓰기, 줄 합치기, JSON 정렬, 목록, 따옴표, 제거, 슬래시, 나누기, 공백 정리, URL 인코딩


## 🗺️ 개선 과제

남은 문제, 직접 확인할 항목, 우선순위별 개선 계획은 [docs/IMPROVEMENTS.md](docs/IMPROVEMENTS.md)에 정리했습니다.


## 🔨 빌드

필요한 것: Windows, [.NET 10 SDK](https://dotnet.microsoft.com/download)

```powershell
dotnet build FlowMemo.WpfUi -c Release
```

실행 파일은 `FlowMemo.WpfUi\bin\Release\net10.0-windows\FlowMemo.exe`에 생깁니다.

- **데이터 위치:** 기본은 `%APPDATA%\FlowMemo\flowmemo.sqlite`입니다. 설정 → 앱 → 데이터에서 폴더를 열 수 있습니다.
- **백업:** 같은 폴더의 `backups\`에 날짜별 백업(`flowmemo-yyyyMMdd.sqlite`, 최근 7개)과 DB 형식을 올리기 직전의 백업(`flowmemo-before-v4-….sqlite`)이 있습니다. 되돌리려면 FlowMemo를 끝내고(알림 영역 아이콘 → 끝내기) 백업 파일을 `flowmemo.sqlite` 자리에 복사합니다.
- **포터블 모드:** `FlowMemo.exe` 옆에 빈 `portable.txt` 파일을 두면 DB를 exe 폴더에 만듭니다.
- **설치 파일(MSI):** `FlowMemo.Setup`은 Visual Studio와 *Microsoft Visual Studio Installer Projects* 확장이 있어야 빌드됩니다.

> WPF 기반이라 Windows에서만 동작합니다.

> **1.x(Pinny Notes)에서 올 때:** 2.0은 처음 실행할 때 DB 형식(스키마 v5)을 올립니다. 올리기 직전에 자동으로 백업하지만, 올린 DB는 1.x에서 열 수 없습니다.


## 🧪 테스트

GitHub Actions 같은 CI는 쓰지 않고, 로컬에서 실행합니다.

```powershell
.\tests\run.ps1              # 전부 (로직 · 데이터 · UI)
.\tests\run.ps1 -Suite data  # 하나만: logic | data | ui
```

- **logic:** 편집 컨트롤과 텍스트 도구를 실제 클래스로 검사합니다.
- **data:** 임시 DB로 생성·마이그레이션(v4→v5)·설정·휴지통·백업을 검사합니다.
- **ui:** 앱 복사본을 띄워 창·메뉴·관리 창·휴지통·설정 창을 UI 자동화로 검사합니다. 테스트하는 동안 FlowMemo 창이 잠깐씩 뜹니다.

Debug 빌드로 실행하므로 평소 쓰는 FlowMemo(Release)가 켜져 있어도 서로 간섭하지 않고, 실제 메모 DB도 건드리지 않습니다. 캡처와 로그는 `%TEMP%\FlowMemo-tests\out`에 남습니다.

화면이 잠겨 있으면 창 캡처가 흰색으로 나옵니다. 그럴 때는 창을 프로세스 안에서 직접 그리는 렌더 모드로 확인합니다:
`tests\FlowMemo.Tests\bin\Debug\net10.0-windows\FlowMemo.Tests.exe render <출력 폴더>`
