local mp = require 'mp'
local function report()
    mp.msg.warn(string.format('Playback motion: video-sync=%s interpolation=%s tscale=%s display-sync-active=%s',
        mp.get_property('video-sync', '?'), mp.get_property('interpolation', '?'),
        mp.get_property('tscale', '?'), mp.get_property('display-sync-active', '?')))
end
mp.register_event('file-loaded', function() mp.add_timeout(0.5, report) end)
mp.observe_property('display-sync-active', 'bool', function(_, value)
    if value ~= nil then report() end
end)
