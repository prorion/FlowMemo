# Change Log


> FlowMemo는 [SemVer](https://semver.org/lang/ko/)(주.부.수)를 따르고, 원본 Pinny Notes 1.x와 구분하려고 **2.0.0**부터 시작한다.
> 버전은 저장소 루트의 `Directory.Build.props` 한 곳에서 관리하고, 릴리스 태그는 `v2.0.0` 형식으로 단다.


## v2.0.0 - 미배포

Pinny Notes v1.17.0을 바탕으로 한 FlowMemo의 첫 버전.

### Added
- 한국어 UI. 메뉴·설정·도구·메시지를 모두 한국어로 바꾸고, 문자열은 `FlowMemo.WpfUi/Resources/Strings.resx` 한 곳에 모았다 (다른 언어는 `Strings.<언어>.resx`로 추가).
- 노트 제목 표시줄에 '더 보기'(⋯) 버튼: 저장·색·메모 관리·설정을 우클릭 없이 쓸 수 있다.
- Ctrl+N으로 새 메모.
- 노트 창 제목을 메모 첫 줄로 표시 (작업 표시줄·Alt+Tab에서 메모를 구분할 수 있다).
- 메모 관리 창: 툴바 버튼 글자, 지금 열려 있는 메모에 '● 열림' 표시, 메모가 없을 때 안내, 조작 안내, Delete/Enter 키, 선택이 없으면 삭제 비활성.
- 휴지통: 관리 창에서 지운 메모는 바로 지우지 않고 30일 보관한다. 휴지통 보기에서 복원·영구 삭제·비우기를 할 수 있고, 카드에 완전히 지워지기까지 남은 날을 표시한다. 휴지통으로 옮길 때는 묻지 않고, 되돌릴 수 없는 영구 삭제·비우기만 확인한다.
- 자동 백업: 시작할 때(켜 둔 동안에도 하루 한 번) `backups\flowmemo-날짜.sqlite`를 만들고 최근 7개를 남긴다. DB 스키마를 올리기 직전에도 따로 백업한다(최근 3개).
- 잠금(읽기 전용) 상태를 저장한다 (예전에는 다시 실행하면 풀렸다).
- 메모마다 만든 시각과 내용을 마지막으로 고친 시각을 기록한다 (정렬·표시는 다음 버전에서).
- 설정 → 앱 → 데이터: DB·백업·오류 기록이 있는 폴더와 [데이터 폴더 열기], 백업으로 되돌리는 방법.
- 트레이로만 실행: 메모·관리·설정 창을 작업 표시줄에 표시하지 않는다 (기본으로 켜짐, 설정 → 앱에서 끌 수 있다).
- 모든 메모 보기/숨기기: 트레이 메뉴와 전역 단축키(기본 Ctrl+Alt+H, 설정 → 앱에서 변경). 숨긴 상태에서 새 메모를 열거나 트레이 아이콘을 누르면 다시 보인다.
- Windows 시작할 때 자동 실행 (설정 → 앱).
- 회귀 테스트 `tests/` (로직·데이터·UI 193개). `tests\run.ps1`로 로컬에서 실행한다.
- 설정 창: "바로 저장됩니다" 안내와 [닫기] 버튼(Esc), 투명도 값(%) 표시, 도구 탭 설명.
- 예기치 않은 오류를 `logs\error.log`에 남기고 한국어로 알린다 (앱이 바로 꺼지지 않는다).
- 화면 배율이 모니터마다 달라도 선명하게 그리는 Per-Monitor V2 DPI 매니페스트.
- 아이콘 버튼에 화면 읽기 프로그램용 이름.
- 색 메뉴에서 지금 색에 ✓ 표시, 고정 버튼은 상태에 따라 툴팁("항상 위에 고정됨 (누르면 해제)")과 진하기가 바뀜.
- 잠금(읽기 전용)을 켜면 제목 표시줄에 자물쇠 표시 (예전에는 표시가 없어 글자가 안 쳐지는 이유를 알 수 없었다).

### Changed
- 프로젝트 이름을 Pinny Notes에서 FlowMemo로 변경 (네임스페이스, 실행 파일 `FlowMemo.exe`, 데이터 폴더 `%APPDATA%\FlowMemo`, DB `flowmemo.sqlite`, 단일 실행 ID, MSI 제품 코드, 업데이트 확인 주소).
- 설정을 바꾸면 바로 저장한다 (예전: 정상 종료할 때만 저장).
- 파일을 메모에 끌어다 놓으면 메모 전체를 덮어쓰지 않고 놓은 위치에 넣는다. CP949(예전 메모장 ANSI) 한글 파일을 인식하고, 바이너리·5MB 초과 파일은 거부한다.
- 날짜/시간 도구가 UTC 대신 현지 시간을 넣는다.
- 잘라내기는 클립보드 복사가 성공한 뒤에만 텍스트를 지운다.
- 관리 창에서 닫힌 메모는 살짝 흐리게 보인다.
- Microsoft.Data.Sqlite 10.0.14로 올림 (SQLite CVE-2025-6965 수정판).
- 도구 메뉴를 영어 알파벳 순서 대신 용도별(정리 · 감싸기·바꾸기 · 인코딩·변환 · 넣기)로 묶고 구분선을 넣음. 설정의 도구 탭도 같은 순서·묶음.
- '끝에 빈 줄 유지'는 입력이 0.3초 멈춘 뒤 적용 (한글 조합 보호).
- 메모 크기 조절 테두리 5 → 7 DIP (잡기 쉽게).
- 메모 제목 표시줄 높이 40 → 28 DIP (메모 크기에 비해 너무 두꺼웠다).
- DB 스키마 v5. 설정을 열 62개짜리 한 행(`Settings`)에서 키/값 테이블(`AppSettings`)로 옮겼다. 이제 설정을 추가할 때 마이그레이션이 필요 없고, 열거형은 이름으로 저장한다. 1.x DB는 처음 실행할 때 자동으로 옮기며, 옮기기 직전에 백업한다. 2.0으로 올린 DB는 1.x에서 열 수 없다.
- 메모 창의 포커스·투명도·색 브러시처럼 저장하지 않는 값이 바뀌어도 메모를 DB에 다시 쓰던 것을 없앴다 (창을 전환할 때마다 쓰기가 일어났다).

### Fixed
- Windows 맞춤법 검사기가 지원하지 않는 언어(예: 한국어)에서 노트 창이 열리지 않던 문제.
- 125% 이상 배율에서 새 메모 위치·설정 창 위치가 어긋나던 문제 (실제 픽셀과 WPF 단위를 섞어 계산).
- 모니터 구성이 바뀌어 화면 밖에 저장된 메모가 보이지 않던 문제.
- 고정(핀) 상태와 실제 '항상 위' 상태가 어긋나는 문제 (주기적으로 맞춤).
- '최소화 허용 안 함'인데 고정하지 않은 메모가 최소화되던 문제.
- 잘못된 Base64·URL 디코딩, 클립보드 잠김, 탭 너비 0, 파일 저장 실패 등으로 앱이 종료되던 문제.
- 해시 도구가 선택 영역이 아니라 메모 전체를 해시하던 문제.
- 색상 도구(HEX→RGB)가 add·face·decade 같은 영어 단어를 RGB로 바꾸던 문제.
- 목록 도구 '목록 기호 지우기'가 일반 줄의 첫 단어를 지우던 문제.
- JSON 정렬이 한글을 `\uXXXX`로 바꾸던 문제.
- 세 번/네 번 클릭 줄 선택이 동작하지 않던 문제.
- 자동 줄 바꿈으로 꺾인 줄에서 자동 들여쓰기가 틀리던 문제.
- 빈 메모에 한글을 입력하면 둘째 글자부터 자모가 흩어지던 문제 ("한글" → "한ㅡㄹ"). '끝에 빈 줄 유지'가 IME 조합 도중에 텍스트를 고쳐서 생겼다. 실제 한글 IME 입력으로 재현하고 고친 뒤 다시 확인.
- 125% 이상 배율에서 관리 창의 '새 메모'가 기존 메모와 정확히 같은 자리에 열려, 기존 메모가 고정돼 있으면 그 뒤에 가려져 보이지 않던 문제.
- 메모가 여러 개일 때 한 메모의 메뉴를 열면 다른 메모 메뉴의 색 견본이 사라지던 문제 (색 견본 하나를 모든 창이 같이 씀).
- 업데이트 확인이 처음 설치한 상태에서 한 번도 돌지 않던 문제.
- 메모 관리 창을 닫으면 설정 창 참조가 지워지던 문제.
- 설정 창이 다시 활성화될 때마다 화면 가운데로 돌아가던 문제.
- 투명도 모드를 바꿔도 아래 항목이 켜지거나 꺼지지 않던 문제.
- 닫힌 노트·관리 창이 메모리에 계속 남던 문제 (메신저 구독 해제).
- 하위 메뉴가 있는 메뉴 항목이 다른 항목보다 왼쪽에 붙어 보이던 문제.
- 공백만 남은 메모가 닫아도 지워지지 않던 문제.
- 알 수 없는 색 이름이 DB에 있으면 메모가 열리지 않던 문제.
- 잠근 메모가 Enter·Tab·붙여넣기·잘라내기·비우기·끌어다 놓기·도구 메뉴로 바뀌던 문제. 잠그면 우클릭 메뉴의 해당 항목도 비활성으로 보인다.
- 메모 관리 창에서 닫은 메모에 '● 열림'이 남고 다음 시작 때 다시 열리던 문제.
- 시작할 때 설정을 읽다가 실패하면 저장된 설정이 기본값으로 덮일 수 있던 문제.
- 여러 줄을 Shift+Tab으로 내어 쓸 때 탭 너비를 무시하고 공백 2~3칸인 줄은 그대로 두던 문제.


> 아래 v1.17.0 이하 기록은 원본 [Pinny Notes](https://github.com/63BeetleSmurf/PinnyNotes)의 변경 이력입니다.


## v1.17.0 - Testing

### Added
- Week Number action to Date Time tool.
- Fluent theme, other windows (settings, management, etc) and menus now match users Windows theme.
- Pinned by default setting.
- Startup and new instance behaviour settings.
- Toolbar to management window.
- Management menu into in note title bar context menu.

### Changed
- Spell checking now done directly with Windows vastly improving performance.

### Fixed
- Notes staying on top of other windows when not pinned.


## v1.16.0 - 23/02/2026

### Added
- GUID tool.
- Auto save notes to database.
- Management Window.

### Changed
- Use exe dir for database when in debug mode.
- Resize can now be done from any side or corner, rather than only with bottom right grip.
- Transparency only enabled if required when loading notes.
- Updated to .NET 10.

### Fixed
- Notes getting stuck as Top Most (Still an ongoing issue, but is better)
- Case Tool, Title case now works when text is all upper case.


## v1.15.0 - 03/10/2025

### Added
- Setting to change note visibility, Show in taskbar, hide in taskbar or hide in taskbar and task switcher (Alt+Tab and Win+Tab).
- Database migration system, seamlessly upgrade the database after upgrading the application.
- Text Lock, toggle Locked from the text box context menu to prevent note text being edited.
- URL tool, encode and decode text for use in URls.
- Marker file check (portable.txt), signal database to be created in exe path when using portable version.

### Changed
- Tray icon implementation, removes dependency on WinForms.

### Fixed
- Notes sometimes not going behind other windows when unpinned.


## v1.14.0 - 30/09/2025

### Added
- Colour tool.
- Backend database.
- Copy (Ctrl+C), Alternative Copy (Ctrl+Shift+C) and Alternative Copy Fallback (Ctrl+Shift+C with no selection) action options.
- Paste (Ctrl+V) and Alternative Copy (Ctrl+Shift+C) action options.
- Trim option for all copy and paste actions.

### Changed
- Code refactoring.
- Rearranged Show In Task Bar and Auto Indent settings.

### Fixed
- Additional copy, cut and paste actions being skipped in some situations, e.g. triggered by mapped mouse button.
- Clipboard being cleared when note if activated and AutoCopy is enabled.
- Child settings not enabling/disabling when parent value changed.
- Undo (Ctrl+Z) not working after applying tool actions.
- Drag drop not working for loading files.


## v1.13.0 - 01/07/2025

### Added
- Settings to change transparency values.
- Settings to change default note size.
- Settings to change fonts.
- Setting to toggle text wrapping.
- Setting to choose copy behaviour when no text selected.

### Changed
- Updated to .NET 9.
- General code refactoring.
- Simplified transparency settings.
- Simplified tool settings.
- Rearranged settings window.

### Fixed
- Occasional crash when pasting from clipboard.


## v1.12.1 - 21/11/2024

### Fixed
- Crash when pressing Shift + Tab at the start of a note.
- Shift + Tab not removing the correct number of spaces.


## v1.12.0 - 20/11/2024

### Added
- Option to check for new version on start up (disabled by default).
- Settings to enable and favourite tools.
- Debug flag allowing release and debug instances to run together.

### Changed
- Moved sorting functions from Lists tool to their own Sort tool.
- Organised settings window.
- Icons updated.

### Fixed
- Notes always turning transparent when activated if Opaque When Focused was off.
- Line count no longer counts trailing empty line when NewLineAtEnd is set and no text is selected.
- Shortcuts not showing icon after install.


## v1.11.3 - 12/11/2024

### Fixed
- Bug where theme was not applied when colour was set to yellow and cycle colours was off.


## v1.11.2 - 03/11/2024

### Changed
- Trigger current running instance to open a new note when a new instance is opened, rather than no action.


## v1.11.1 - 23/10/2024

### Added
- Setting to toggle showing notes in Taskbar.

### Fixed
- Settings window opening off screen when note near or over the edge of the screen.
- Revert mouse button swapped check. Handled already by windows so reported bug must have been a mistake.


## v1.11.0 - 22/10/2024

### Added
- Tray icon - Click to show open notes, double click to create a new note and right click for menu.
- Bracket tool.
- Backtick action to Quote tool.
- Trim action to Quote tool.
- Ability to use Shift + Tab to remove whitespace.
- Setting to hide the title bar on note.
- Setting to use spaces instead of tabs when Tab is pressed.
- Setting to use a mono spaced font.
- Title bar menu item to reset note back to default size.

### Changed
- Minimum note size.

### Fixed
- Title bar only being draggable with left mouse button even when buttons switched in Windows.
- Save prompt did now show if a note was closed by right clicking in Windows Taskbar.
- Notes would always open on primary monitor, even if opened from parent on another monitor.


## v1.10.0 - 22/07/2024

### Added
- Minimize settings - Specify if notes should prevent themselves being minimized, e.g. with Show Desktop.
- Tab indent - Selecting text and pressing tab will now indent the selected text.
- Remove tool - Remove various whitespace, slashes or occurrences of current selection.
- Settings window - Settings move out of menu and into their own window.
- Start positions - More start positions have been added.
- Transparency - Note can now be transparent depending on given settings.
- Dark mode - Notes can appear dark with the selected colour used as accent colours.

### Fixed
- Notes order - Unpinned notes now come above pinned notes when focused for editing.
- Dragging issue - Fixed issue where dragging a word would overwrite note text.


## v1.9.0 - 11/07/2024

### Added
- Gibberish Tool - Generate gibberish/sample text from words to full articles.
- DateTime Tool - Initial implementation, more date functions to be added.

### Changed
- Colour menu now shows a preview of the colour.
- Performance improvements.
- Various code refactoring and optimisations in preparation to move to a MVVM architecture.

### Fixed
- Fixed spelling suggestions not showing in context menu.


## v1.8.0 - 09/07/2024

### Added
- Slash tool: Toggle, replace or remove forward and back slashes.

### Changed
- Various code refactoring to ease future work towards new major version.


## v1.7.0 - 05/05/2023

### Added
- Counts menu to show line, word and char counts.
- Quote tools to wrap text in single or double quotes.
- Trim tool to remove blank lines.
- List tools for dashed list (markdown) and to remove list markers.
- Keep new line at end visible.
- Middle click to paste.
- Auto indent.
- Hold Ctrl to copy when selecting text by mouse clicks.
- Toggle maximize when title bar double clicked.


## v1.6.0 - 15/04/2023

### Added
- Added split tool.
- Added join tool.

### Changed
- Sort tools menu alphabetically.
- Move caret to end of text after change.

### Fixed
- Remember save state of note.
- Fix tools to work with full note text or selected text.
- Text box accepts tab.


## v1.5.0 - 12/04/2023

### Added
- All tools now work on selected note text or all text if nothing selected.
- Text case options.
- Options to trim text copied from or pasted into a note.
- Triple click to select full line, quadruple click to ignore wrapping and select entire line.

### Changed
- Moved tools into text box context menu.
- Checking for updates is now opt-in.


## v1.4.0 - 16/03/2023

### Added
- HTML entity encode and decode options.

### Changed
- Rearrange menu, colours now easier to access.
- Adjust positioning so it is easier to get notes behind others.

### Fixed
- Only update gravity after drag stopped.
- Do not add new line when note is empty.
- Fix screen bounds check.


## v1.3.0 - 11/03/2023

### Added
- Note colours with cycle colours option.
- Option to always ensure text ends with new line.
- Start-up position option.

### Changed
- Centre dialogs on parent note.
- Don't enable Auto Copy by default.
- Leave extra room for title bar with upward gravity as well as downward.


## v1.2.1 - 09/03/2023

### Fixed
- Settings were not loading when creating new notes.


## v1.2.0 - 08/03/2023

### Added
- Weekly check for new releases.
- Border to main window.
- Drag and drop handling for text and files.
- Hashing options.

### Changed
- Move save option into context menu.
- Moved settings to sub menu.

### Fixed
- JSON error handling, leave text unchanged if invalid.


## v1.1.0 - 07/03/2023

### Added
- JSON prettify option.
- Spell checker option.
- Option to clear note.
- List enumerate option and sorting options.
- Indent options.
- Base64 Encoding and Decoding.
- Trim option to menu.
- Auto copy option.

### Changed
- Swap icons for pinned and unpinned to make more sense.

### Fixed
- Focus on text box when loaded.
