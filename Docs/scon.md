# Scon - Server CONsole

## 요약
웹에서 게임 서버의 콘솔에 접근하는 프로젝트.

- Discord OAuth로 로그인, 특정 길드의 특정 역할이 있다면 로그인 가능.
- 로그인한 유저에게 서버 콘솔 내용을 실시간으로 보내 주는 API. (Server-Sent Messages 활용)
- 로그인한 유저로부터 명령어를 입력받는 API.


## 환경 변수
- `BASE_URL` : `https://some.example.org`과 같은 형식의 사이트 베이스 도메인. OAuth 리다이렉트 URL을 만들 때 쓰인다.
- OAuth 관련:
    - `DISCORD_CLIENT_ID`
    - `DISCORD_CLIENT_SECRET`
    - `DISCORD_AUTHORIZED_GUILD_ID`
    - `DISCORD_AUTHORIZED_ROLE_ID`
- 게임 서버와의 상호작용 관련:
    - `SCON_TAIL_CMD`
    - `SCON_SEND_CMD`


## 유저 인증에 대해

관련 환경 변수:
- `DISCORD_CLIENT_ID`
- `DISCORD_CLIENT_SECRET`
- `SCON_AUTHORIZED_GUILD_ID`
- `SCON_AUTHORIZED_ROLE_ID`

엔드포인트:
- GET `/login` : Discord의 로그인 요청 페이지로 리다이렉트.
- POST `/logout` : 로그아웃.
- `/oauth` : OAuth에 전달할 리다이렉트 URI.

유저는 모든 API를 사용하기 전에, 우선 Discord OAuth로의 로그인 과정을 거쳐야 한다.
이 때, OAuth의 구현은 `AspNet.Security.OAuth.Discord` 모듈을 사용한다.
필요한 스코프는 `identify`와 `guilds.members.read`이다.

Discord OAuth로의 인증이 완료되었다면, 아래 체크를 수행한다.
이 과정에서 Discord API로의 추가적인 API 호출이 필요할 수 있다.

- 해당 디스코드 계정은 `SCON_AUTHORIZED_GUILD_ID` 길드에 가입해 있어야 한다.
- 해당 디스코드 계정은 위 길드에서 `SCON_AUTHORIZED_ROLE_ID` 역할을 가지고 있어야 한다.

하나라도 만족하지 않는다면, 해당 유저는 로그인할 수 없다. 403 Forbidden으로 응답한다.

둘 모두 만족한다면, 로그인할 수 있다. 해당 유저의 길드 내 닉네임 (이하 '유저네임')을 가져온다.

**주의:** 유저네임에서 **반드시** 큰따옴표("), 줄바꿈(\n), 세미콜론(;)을 **전부** 제거하라. 만약 유저네임이 16자를 넘는다면, 16자로 잘라라.

60분 이상 지난 로그인 세션은, 위의 검증 과정을 다시 거쳐야 한다.


## 게임 서버와의 통신

### 콘솔 내용을 제공받는 방법

관련 환경 변수:
- `SCON_TAIL_CMD`

엔드포인트:
- GET `/api/console` (Server Sent Event 형식으로 보내준다.)

환경 변수로 입력받은 특정한 커맨드를 실행한다. 해당 커맨드가 stdout으로 출력하는 내용을, 그대로 사용자에게 제공해주면 된다.
- 해당 커맨드는 스스로 종료되지 않고, 콘솔 내용을 계속 출력한다.
- 해당 커맨드의 출력 형식은 서버 로그를 `tail -F`하는 것과 유사하다.
- 로그 로테이션이나 연결 끊김 대응 같은 처리는, 해당 커맨드가 처리해 준다고 가정한다.
- stderr으로 출력되는 내용은, 만약 있다면, stdout처럼 병합해서 처리한다.

주의사항은 다음과 같다.
- 해당 커맨드는 Scon 시작 시점에 하나만 실행하고 single source of truth로 삼아야 한다.
    - 여러 번 실행해도 출력은 같지만, 오버헤드가 있기 때문이다.

### 플레이어 목록

콘솔 페이지 오른쪽에 게임 서버의 현재 플레이어 목록을 보여준다.

게임 서버는 `SCON_TAIL_CMD`가 읽는 로그에 아래 형식의 줄을 주기적으로(그리고 접속/퇴장 시) 남긴다.
줄 앞에 타임스탬프 등 다른 내용이 있어도 된다.

```
[SCON_PLAYERS] <Base64(JSON 배열)>
```

JSON 배열의 각 원소는 `{ "name": "닉네임", "steamid": "STEAM_0:1:1234" }` 형식이다.

- Scon은 이 줄을 해석해 플레이어 목록을 갱신하고, 콘솔 출력과 히스토리에서는 숨긴다.
- 해석에 실패한 줄은 디버깅을 위해 일반 로그 줄로 그대로 보여준다.
- 목록은 SSE의 `players` 이벤트(`{ "players": [...], "updatedAt": "..." }`)로 전달된다.
- Garry's Mod용 예시 스크립트: `Docs/scon_players.lua`

### 유저로부터 입력받은 명령어의 해석

엔드포인트:
- POST `/api/command` (body는 명령어 문자열)

유저로부터 입력받은 모든 명령은, 특정한 해석 과정을 거쳐서 서버가 인식할 수 있는 형태로 바꾸어야 한다.

아래는 유저가 입력할 수 있는 명령 및 그에 대응하는 서버 전달 커맨드의 목록이다.

**주의:** 게임 서버에 전달되는 텍스트는 1024 바이트를 넘기지 않게 하라.

**주의:** 유저로부터 전달받은 **모든** 텍스트에서, **큰따옴표, 세미콜론, 줄바꿈 문자를 반드시 전부 제거하라!** 그렇지 않으면 임의 코드 실행의 가능성이 있다.

만약 커맨드 파라미터에 글자수 제한이 있다면, 넘친 글자는 자른다.

- `say [text...]`
    - `scon_say "[유저네임]" "[text...]"`
    - [text...]의 제한은 200글자.
    - [text...]는 Base64 인코딩하여 보낸다.

- `status`
    - `scon_status "[유저네임]"`

- `kick [target] [reason...]`
    - `scon_kick "[유저네임]" "[target]" "[reason...]"`
    - [target]은 대상의 닉네임 일부분이다. 제한은 32글자.
    - [reason...]은 킥 사유이다. 제한은 64글자.
    - [target]와 [reason...]은 Base64 인코딩하여 보낸다.

- `banid [steamid] [duration] [reason...]`
    - `scon_ban "[유저네임]" "[steamid]" "[duration]" "[reason...]"`
    - [steamid]는 SteamID이다. 최대 32글자.
    - [duration]은 문자열로 나타낸 밴 기간이다. 최대 20글자.
    - [reason...]은 밴 사유이다. 최대 64글자.
    - [reason...]은 Base64 인코딩하여 보낸다.

### 게임 서버로의 커맨드 입력

관련 환경 변수:
- `SCON_SEND_CMD`

환경 변수로 주어진 명령에, 실행할 커맨드를 **Base64 인코딩해서** 제공하면 게임 서버로 커맨드가 전달된다.
예를 들어, `$SCON_SEND_CMD SGVsbG8gd29ybGQKSGVsbG8gd29ybGQK` 과 같은 형식으로 실행하면 된다.

## 기타 사항
본 프로젝트는 .NET의 Native AOT를 사용하는 것을 선호한다.
그러나, 기술적인 문제로 그렇게 하는 것이 불가능하다면, 최후의 수단으로 Native AOT 지원을 제거하는 것도 가능하다.
