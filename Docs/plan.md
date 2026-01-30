# Implementation Plan: Scon

## Phase 1: Configuration & Skeleton
**Goal**: Set up the environment infrastructure and ensure settings are readable.

1.  **Environment Variables**:
    - Create a typed configuration class `SconOptions` to map the required environment variables (`BASE_URL`, `DISCORD_*`, `SCON_*`).
    - Register this configuration in DI.
2.  **Logging**: Ensure Console logging is configured to help debug the background services later.

## Phase 2: Authentication (Discord OAuth)
**Goal**: Secure the application so only specific users can access the endpoints.

1.  **Setup Auth Middleware**:
    - Configure `AddAuthentication` with Cookie and Discord schemes in `Program.cs`.
    - Configure `AspNet.Security.OAuth.Discord`.
2.  **Access Control Policy**:
    - Implement a custom `OAuthEvents` handler or a middleware that runs *after* a successful Discord login.
    - **Logic**:
        - Use the Access Token to call Discord API: `GET /users/@me/guilds/{guild.id}/member`.
        - Verify the user is in the guild defined by `DISCORD_AUTHORIZED_GUILD_ID`.
        - Verify the user has the role defined by `DISCORD_AUTHORIZED_ROLE_ID`.
        - If failed, return 403.
        - If success, persist the "Sanitized Username" (stripped of special chars, max 16 chars) into the User Claims identity.
3.  **Endpoints**:
    - `GET /login`: Triggers the challenge.
    - `POST /logout`: Signs out the cookie.

## Phase 3: Background Console Service
**Goal**: Manage the single source of truth for the game server console output.

1.  **ConsoleService (BackgroundService)**:
    - Create a class inheriting `BackgroundService`.
    - On start, spawn the process defined in `SCON_TAIL_CMD`.
    - Asynchronously read `StandardOutput` (and optional `StandardError`).
2.  **Broadcasting**:
    - Use `System.Threading.Channels` or a simpler specific `event` mechanism to broadcast new log lines.
    - Implement a "History Buffer" (e.g., a Queue of the last 50 lines) so new users see immediate context upon joining.

## Phase 4: API - Server-Sent Events (SSE)
**Goal**: Real-time delivery of console logs to the browser.

1.  **Endpoint**: Implement `GET /api/console`.
2.  **Logic**:
    - Verify Authentication.
    - Set Response Content-Type to `text/event-stream`.
    - Subscribe to the `ConsoleService` broadcast.
    - Loop and write data to the `HttpContext.Response` as it arrives.
    - Handle cancellation (when user closes tab).

## Phase 5: API - Command Execution
**Goal**: Parse and forward user commands securely.

1.  **Command Processor Strategy**:
    - Create an interface `ICommandParser` or a simple Strategy pattern.
    - Implement the parsing logic for: `say`, `status`, `kick`, `banid`.
    - **Sanitization**: STRICT enforcement of the "no quotes, no newlines, no semicolons" rule.
    - **Formatting**: Convert user input into the server-specific string format (e.g., `scon_say "User" "Msg"`).
2.  **Execution endpoint**:
    - `POST /api/command`.
    - Accept text body.
    - Pass through Sanitizer -> Parser.
    - Encode the final command string to Base64.
    - Execute `SCON_SEND_CMD` with the Base64 argument using `Process.Start`.
3.  **Validation**: Return strict 400 errors if inputs are too long or invalid.

## Phase 6: Frontend (Basic)
**Goal**: Minimal UI to test the functionality.

1.  **View**:
    - Update `wwwroot/index.html` with a console log `<div>` and an input `<form>`.
2.  **Scripts**:
    - JS `EventSource` to connect to`/api/console`.
    - JS `fetch` to POST to `/api/command`.