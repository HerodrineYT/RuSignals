import struct, zlib
from pathlib import Path

# Binary MSTS shape token IDs; no dependency on the route editor's files.
names = dict(enumerate('''reserved comment point vector quat normals normal_idxs
points uv_point uv_points colour colours packed_colour image images texture
textures light_material light_materials linear_key tcb_key linear_pos tcb_pos
slerp_rot tcb_rot controllers anim_node anim_nodes animation animations anim
lod_controls lod_control distance_levels_header distance_level_header
dlevel_selection distance_levels distance_level sub_objects sub_object
sub_object_header geometry_info geometry_nodes geometry_node geometry_node_map
cullable_prims vtx_state vtx_states vertex vertex_uvs vertices vertex_set
vertex_sets primitives prim_state prim_states prim_state_idx indexed_point_list
point_list indexed_line_list indexed_trilist tex_idxs tri vertex_idxs flags
matrix matrices hierarchy volumes vol_sphere shape_header shape shader_names
shader_name texture_filter_names texture_filter_name sort_vectors uvop_arg_sets
uvop_arg_set light_model_cfgs light_model_cfg'''.split()))
names.update({125: 'named_filter_mode', 129: 'named_shader'})

class Block:
    def __init__(self, b, at):
        self.id, self.flags, length = struct.unpack_from('<HHI', b, at)
        self.name = names.get(self.id, str(self.id))
        self.end = at + 8 + length
        n = b[at + 8]
        self.label = b[at+9:at+9+2*n].decode('utf-16-le')
        self.data = b[at+9+2*n:self.end]
    def children(self, skip=0):
        result = []
        while skip < len(self.data):
            block = Block(self.data, skip)
            result.append(block)
            skip = block.end
        assert skip == len(self.data), (self.name, skip, len(self.data))
        return result

def shape(path):
    b = Path(path).read_bytes()
    d = zlib.decompress(b[16:]) if b[:8] == b'SIMISA@F' else b[16:]
    assert d[:8] == b'JINX0s1b', d[:16]
    return Block(d, 16)

if __name__ == '__main__':
    import sys
    root = shape(sys.argv[1])
    for c in root.children():
        print(c.id, c.name, len(c.data))
        if c.name in ('matrices','images','textures','shader_names','prim_states','vtx_states','lod_controls'):
            for s in c.children(4):
                print(' ', s.name, s.label, len(s.data), s.data[:100])
