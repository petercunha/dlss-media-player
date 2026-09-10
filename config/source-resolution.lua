-- Pass only the rendered video rectangle, never frames or URLs, to the feeder.
-- The child-scoped path is unique for each player/Streamlink invocation.
local path = os.getenv('DLSS_MEDIA_GEOMETRY')
if not path or path == '' then return end
local last = ''
local function publish()
    local d = mp.get_property_native('osd-dimensions')
    local p = mp.get_property_native('video-out-params')
    local value = '0 0 0 0 0 0 0 0\n'
    if d and p and d.w and d.h and d.ml and d.mr and d.mt and d.mb then
        value = string.format('%d %d %d %d %d %d %d %d\n', d.w, d.h,
            d.ml, d.mt, d.w-d.ml-d.mr, d.h-d.mt-d.mb, p.dw or p.w, p.dh or p.h)
    end
    if value == last then return end
    local f = io.open(path, 'w')
    if f then f:write(value); f:close(); last = value end
end
mp.observe_property('osd-dimensions', 'native', publish)
mp.observe_property('video-out-params', 'native', publish)
mp.register_event('end-file', function()
    local f = io.open(path, 'w')
    if f then f:write('0 0 0 0 0 0 0 0\n'); f:close() end
    last = ''
end)
mp.register_event('shutdown', function() os.remove(path) end)
