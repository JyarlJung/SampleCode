//spatial쉐이더는 일반적인 3D물체를 나타낼 때 사용하는 타입입니다.
shader_type spatial;


const float z_near = 0.05;
const float z_far = 4000.0;
uniform sampler2D depth_texture : hint_depth_texture,filter_nearest;
uniform sampler2D screen_texture : hint_screen_texture,filter_nearest;

group_uniforms WaterSetting;
uniform vec3 water_color:source_color = vec3(0.12,0.25,0.32);
uniform vec3 border_color:source_color = vec3(0.4,0.5,0.6);
uniform float border_scale:hint_range(0.00, 0.1, 0.001) = 0.0;
uniform float roughness:hint_range(0.0, 1.0, 0.01) = 0.05;
uniform float specular:hint_range(0.0, 2.0, 0.01) = 0.5;
uniform float fade_distance:hint_range(0.0, 10.0, 0.01) = 5.0;

//큰 규모의 Wave를 나타내는 텍스쳐(대표적으로 Perlin Noise)를 height_tex로 받습니다.
group_uniforms HeightMap;
uniform sampler2D height_tex:filter_linear;
uniform vec2 move_vec;
uniform float height_strength:hint_range(0.0, 1.0, 0.01) = 0.1;

//작은 규모의 Wave를 나타내는 텍스쳐를 wave_tex로 받습니다.
group_uniforms NormalMap;
uniform sampler2D wave_tex:filter_linear;
uniform vec2 normal_move_vec;
uniform float normal_strength:hint_range(0.0, 1.0, 0.01) = 0.1;
uniform float refraction_value:hint_range(0.0, 0.4, 0.01) = 0.05;

//ssr_max_travel은 SSR을 수행하기 위한 iteration의 최대 단계입니다.
group_uniforms SSRSetting;
uniform int ssr_max_travel = 24;
uniform float step_pixel_size = 6.0;
uniform float depth_tolerance = 0.2;

//Ripple효과를 나타내는 offscreen buffer를 simulation로 받습니다.
//카메라에서 depth_clip이상 떨어진 경우 나타내지 않습니다.
group_uniforms SplashSetting;
uniform sampler2D simulation;
uniform vec2 collision_size;
uniform vec2 subdivision;
uniform float splash_strength = 1.5;
uniform float depth_clip = 25.0;

//res는 Viewport의 해상도를 나타냅니다.
global uniform bool refl;
global uniform int res;

//Screen Space의 좌표를 Camera Space로 역변환합니다.
vec3 uv_to_view(vec2 uv, float depth, mat4 inv_proj_m)
{
	vec4 position_ndc = vec4((uv * 2.0) - 1.0, depth, 1.0);
	vec4 view_position = inv_proj_m * position_ndc;
	return view_position.xyz / view_position.w;
}

//Camera Space의 좌표를 Screen Space로 변환합니다.
vec3 view_to_uv(vec3 position_view_space, mat4 proj_m, out float w)
{
	vec4 position_clip_space = proj_m * vec4(position_view_space.xyz, 1.0);
	vec3 position_ndc = position_clip_space.xyz / position_clip_space.w;
	w = position_clip_space.w;
	return vec3(position_ndc.xy * 0.5 + 0.5, position_ndc.z);
}

//SSR을 수행하는 핵심 함수입니다.
//pos : 초기 Ray의 위치
//advance : step별로 진행되는 크기
//반사되는 부분을 찾은 경우 그 부분의 color, 찾지 못한 경우 vec4(0.0)를 반환합니다.
vec4 get_depth_tested_color(vec4 pos, vec4 advance, vec2 viewport_size, mat4 inv_mat){
	vec2 pixel_size = 1.0 / viewport_size;
	vec2 init_pos = pos.xy;

	//z의 경우 linear interpolation이 아닌 depth interpolation을 수행합니다.
	float z_from = pos.z / pos.w;
	float z_to = z_from;

	vec2 uv;
	float depth;
	for(int i = 0; i < ssr_max_travel * res; i++)
	{
		pos += advance;
		uv = (pos.xy - 0.5) * pixel_size;
		depth = uv_to_view(uv,texture(depth_texture,uv).r, inv_mat).z;

		//z의 경우 linear interpolation이 아닌 depth interpolation을 수행합니다.
		z_from = z_to;
		z_to = pos.z / pos.w;
		
		if(depth > z_to && depth <= max(z_to, z_from) + depth_tolerance && -depth < z_far * 0.95)
		{
			if (any(bvec4(lessThan(pos.xy, vec2(1.0, 1.0)), greaterThan(pos.xy,viewport_size))) == false) {
			return vec4(texture(screen_texture, (pos.xy-0.5) * pixel_size).rgb,1.0);
			}
		}
	}
	return vec4(0.0);
}

//vertex쉐이더의 경우 height_tex로 displacement를 수행하고 normal을 변환합니다.
void vertex() {
	mat3 tbn = mat3(TANGENT,BINORMAL,NORMAL);
	vec2 vertex_uv = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xz;
	vertex_uv = (vertex_uv * 0.02)+(TIME * 0.02 * move_vec);
	
	float Bu = texture(height_tex,vertex_uv + vec2(0.02,0.0)).r -
	texture(height_tex, vertex_uv - vec2(0.02,0.0)).r;
	float Bv = texture(height_tex,vertex_uv + vec2(0.0,0.02)).r - 
	texture(height_tex, vertex_uv - vec2(0.0,0.02)).r;
	
	vec3 bump_vec = vec3(-Bu*height_strength, -Bv*height_strength, 1.0);
	float height = texture(height_tex,vertex_uv).x * 2.0 -1.0;
	height = height * height_strength;
	
	VERTEX.y += height;
	NORMAL = normalize(tbn*bump_vec);
}


void fragment() {

	//wave_tex로 normal map을 적용합니다.
	mat3 tbn = mat3(TANGENT,BINORMAL,NORMAL);
	vec3 normal_map = texture(wave_tex,(UV * 0.5)+(TIME * 0.05 * normal_move_vec)).xyz * 2.0 - 1.0;
	normal_map = vec3(normal_map.xy * normal_strength, 1.0);

	
	vec2 splash_uv = UV * subdivision;
	splash_uv.y=-splash_uv.y;
	vec3 duv = vec3(2.5 /collision_size.x,2.5 /collision_size.y,0.0);
	
	float Bu = texture(simulation,splash_uv - duv.xz).y -
	texture(simulation, splash_uv + duv.xz).y;
	float Bv = texture(simulation,splash_uv - duv.zy).y - 
	texture(simulation, splash_uv + duv.zy).y;
	vec3 bump_vec = -depth_clip < VERTEX.z ? vec3(Bu*splash_strength, Bv*splash_strength, 1.0) : vec3(0.0);
	
	//최종적으로 계산된 normal입니다.
	vec3 normal = normalize(tbn * (normal_map+bump_vec));
	
	//SSR을 수행하기 위한 준비입니다.
	vec4 reflect_color = vec4(0.0);
	vec4 refract_color = vec4(0.0);
	vec3 surface_color = water_color;
	
	vec2 pixel_size = 1.0 / VIEWPORT_SIZE;
	vec3 vertex = VERTEX;
	
	//view ray와 반사 ray를 계산합니다.
	vec3 view_dir = normalize(vertex);
	vec3 ray_dir = normalize(reflect(view_dir,normal));

	//반사 ray를 카메라frustum에 맞춰 자릅니다.
	float ray_len = (vertex.z + ray_dir.z * z_far) > - z_near ? (-z_near - vertex.z) / ray_dir.z : z_far;
	vec3 ray_end = vertex + ray_dir * ray_len;
	
	//depth interpolation을 수행하기 위한 w값을 계산합니다.
	float w_begin;
	vec3 vp_line_begin = view_to_uv(vertex,PROJECTION_MATRIX,w_begin);
	float w_end;
	vec3 vp_line_end = view_to_uv(ray_end,PROJECTION_MATRIX,w_end);
	vec2 vp_line_dir = vp_line_end.xy - vp_line_begin.xy;
	
	w_begin = 1.0 / w_begin;
	w_end = 1.0 / w_end;
	
	float z_begin = vertex.z * w_begin;
	float z_end = ray_end.z * w_end;
	
	vec2 line_begin = vp_line_begin.xy / pixel_size;
	vec2 line_dir = vp_line_dir / pixel_size;
	float z_dir = z_end - z_begin;
	float w_dir = w_end - w_begin;
	
	//ray의 끝이 viewport 밖으로 나가는 경우 그 길이를 조정합니다.
	float scale_max_x = min(1.0, 0.99 * (1.0 - vp_line_begin.x) / max(1e-5, vp_line_dir.x));
	float scale_max_y = min(1.0, 0.99 * (1.0 - vp_line_begin.y) / max(1e-5, vp_line_dir.y));
	float scale_min_x = min(1.0, 0.99 * vp_line_begin.x / max(1e-5, -vp_line_dir.x));
	float scale_min_y = min(1.0, 0.99 * vp_line_begin.y / max(1e-5, -vp_line_dir.y));
	float line_clip = min(scale_max_x,scale_max_y) * min(scale_min_x,scale_min_y);
	
	line_dir *= line_clip;
	z_dir *= line_clip;
	w_dir *= line_clip;
	
	//ray가 대각선 방향인 경우, 1픽셀 길이씩 이동할 때 같은 픽셀을 선택하게 됩니다. 그 경우를 위해 step size를 조정합니다.
	float advance_angle_adj = 1.0 / max(abs(normalize(line_dir).x), abs(normalize(line_dir).y));
	float step_size = step_pixel_size / length(line_dir);
	vec4 line_advance = vec4(line_dir, z_dir, w_dir) * advance_angle_adj * step_size;
	vec4 init_pos = vec4(line_begin, z_begin, w_begin);

	//물 속의 물체에 대해 굴절된 uv를 계산합니다.
	vec2 refract_uv = normal_map.xy * refraction_value;
	float depth_is_back = sign(texture(depth_texture,SCREEN_UV + refract_uv).r-FRAGCOORD.z);
	refract_uv = SCREEN_UV + (clamp(depth_is_back,0.0,1.0) * refract_uv);
	
	vec3 refract_screen_pos = uv_to_view(refract_uv,texture(depth_texture,refract_uv).r, INV_PROJECTION_MATRIX);
	float water_depth = sign(vertex.z - refract_screen_pos.z) * length(vertex - refract_screen_pos);
	water_depth = clamp(water_depth / fade_distance,0.0,1.0);
	water_depth = pow(water_depth, 2.0);
	
	refract_color.rgb = texture(screen_texture,refract_uv).rgb;
	refract_color.rgb = mix(border_color, refract_color.rgb, clamp(water_depth / border_scale,0.5,1.0));
	
	//SSR을 수행합니다.
	if(refl)
	{
		reflect_color = get_depth_tested_color(init_pos, line_advance, VIEWPORT_SIZE, INV_PROJECTION_MATRIX);
	}
	//반사된 색상에 대한 fresnel효과를 적용합니다.
	float reflect_amount = reflect_color.a * pow(1.0-dot(normal,VIEW),3.0);
	
	//반사된 생상과 물의 원래 색상을 섞어줍니다.
	surface_color = mix(vec3(0.0),water_color,water_depth);
	surface_color = mix(surface_color, vec3(0.0), reflect_amount);
	
	float final_specular = specular * pow(1.0-dot(normal,VIEW),3.0);;
	final_specular = mix(final_specular, 0.0, reflect_amount);
	
	//반사된 색상은 '이미 쉐이딩된' 색상이기 때문에, 두번 쉐이딩하는걸 방지하기 위해 albedo가 아닌emission으로 받습니다.
	vec3 emission = mix(refract_color.rgb,vec3(0.0),water_depth);
	emission = mix(emission, reflect_color.rgb, reflect_amount);

	ALBEDO = surface_color;
	ROUGHNESS = roughness;
	SPECULAR = final_specular;
	NORMAL = normal;
	EMISSION = emission;
}


