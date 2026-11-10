-- Scon player list reporter (Garry's Mod)
-- Place in: garrysmod/addons/<addon>/lua/autorun/server/scon_players.lua
--
-- Writes a line like
--   [SCON_PLAYERS] W3sibmFtZSI6IuyEnOuyhCIsInN0ZWFtaWQiOiJTVEVBTV8wOjE6MTIzNCJ9XQ==
-- to the log that SCON_TAIL_CMD reads. Scon decodes it, updates the player list
-- on the console page, and hides the line from the console output.
--
-- The payload is Base64(JSON array of { name, steamid }).

local REPORT_INTERVAL = 30 -- seconds; also reported on join/leave

local function ReportPlayers()
    local list = {}
    for _, ply in ipairs(player.GetHumans()) do
        list[#list + 1] = { name = ply:Nick(), steamid = ply:SteamID() }
    end

    -- util.TableToJSON({}) gives "[]", which is what we want for an empty server
    local line = "[SCON_PLAYERS] " .. util.Base64Encode(util.TableToJSON(list), true)

    if ulx and ulx.logString then
        -- SCON_TAIL_CMD tails the ULX log directory (see log_tracker_example.sh)
        ulx.logString(line)
    else
        -- Fallback: server console / logs/ directory
        ServerLog(line .. "\n")
    end
end

timer.Create("SconPlayersReport", REPORT_INTERVAL, 0, ReportPlayers)

-- Delay a moment so the joining player is listed / the leaving player is gone
hook.Add("PlayerInitialSpawn", "SconPlayersReport", function()
    timer.Simple(1, ReportPlayers)
end)

hook.Add("PlayerDisconnected", "SconPlayersReport", function()
    timer.Simple(1, ReportPlayers)
end)
