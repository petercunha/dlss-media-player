local mp = require 'mp'
local options = {width=3840, height=2160}
require('mp.options').read_options(options, 'live_vsr')
local last = ''
mp.observe_property('video-dec-params', 'native', function(_, params)
    if not params or not params.w or not params.h then return end
    local key = params.w .. 'x' .. params.h
    if key == last then return end
    last = key
    -- Work on decoder frames, before MPV scales them to its window. Avoid the
    -- previous RGB round trip and avoid accidentally combining VSR with DLSS.
    local scale = math.min(options.width / params.w, options.height / params.h, 4)
    if scale <= 1 then
        mp.commandv('vf', 'remove', '@live_vsr')
        mp.msg.warn('RTX VSR: no enlargement needed at this output size')
        return
    end
    local filter = string.format('@live_vsr:d3d11vpp=scale=%.6f:scaling-mode=nvidia', scale)
    local ok = mp.commandv('vf', 'add', filter)
    if ok then
        mp.msg.warn(string.format('RTX VSR requested: %dx%d -> %.0fx%.0f; DLSS disabled', params.w, params.h, params.w*scale, params.h*scale))
    else
        mp.msg.error('RTX VSR filter failed; using ordinary MPV scaling')
    end
end)
