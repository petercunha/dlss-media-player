local mp = require 'mp'
mp.add_key_binding('F8', 'toggle-display-smoothing', function()
    local enabled = not mp.get_property_bool('interpolation', false)
    mp.set_property('video-sync', enabled and 'display-resample' or 'audio')
    mp.set_property_bool('interpolation', enabled)
    if enabled then mp.set_property('tscale', 'oversample') end
    mp.osd_message(enabled and 'Display smoothing ON (not RIFE)' or 'Display smoothing OFF', 3)
end)
