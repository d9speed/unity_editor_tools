using System;
using System.Linq;
using TMPro;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace D9speed_BaseEditorUtils.GuideBoard
{
    [Serializable]
    internal sealed class guide_board_settings
    {
        public string text = "SETUP GUIDE\n\n説明文をここに入力します。";
        public TMP_FontAsset font;
        public int width = 1024;
        public int height = 512;
        public int font_size = 48;
        public int padding = 40;
        public Color text_color = new Color(0.08f, 0.08f, 0.08f, 1f);
        public Color background_color = Color.white;
        public int alignment;

        public void normalize()
        {
            width = Mathf.Clamp(width, 64, 2048);
            height = Mathf.Clamp(height, 64, 2048);
            font_size = Mathf.Clamp(font_size, 8, 256);
            padding = Mathf.Clamp(padding, 0, (Mathf.Min(width, height) - 16) / 2);
            alignment = Mathf.Clamp(alignment, 0, 2);
            text_color.a = 1f;
            background_color.a = 1f;
        }
    }

    internal sealed class guide_board_preview : IDisposable
    {
        private Scene preview_scene;
        private Camera preview_camera;
        private GameObject board;
        private Mesh board_mesh;
        private Material board_material;
        private Material text_material;
        private TextMeshPro text_mesh;
        private RenderTexture render_texture;
        private TMP_FontAsset current_font;

        public Texture texture => render_texture;
        public int render_count { get; private set; }
        public bool text_overflows { get; private set; }
        public bool has_missing_characters { get; private set; }

        public guide_board_preview()
        {
            if (GraphicsSettings.currentRenderPipeline != null)
                throw new InvalidOperationException("このツールは Built-in Render Pipeline 用です。");

            try
            {
                preview_scene = EditorSceneManager.NewPreviewScene();
                var camera_object = create_object("guide_camera", typeof(Camera));
                preview_camera = camera_object.GetComponent<Camera>();
                preview_camera.enabled = false;
                preview_camera.scene = preview_scene;
                preview_camera.cameraType = CameraType.Preview;
                preview_camera.orthographic = true;
                preview_camera.transform.position = new Vector3(0f, 0f, -10f);
                preview_camera.nearClipPlane = 0.1f;
                preview_camera.farClipPlane = 20f;
                preview_camera.clearFlags = CameraClearFlags.SolidColor;
                preview_camera.allowHDR = false;
                preview_camera.allowMSAA = false;
                preview_camera.useOcclusionCulling = false;

                board = create_object("guide_board", typeof(MeshFilter), typeof(MeshRenderer));
                board_mesh = new Mesh { name = "guide_quad", hideFlags = HideFlags.HideAndDontSave };
                board_mesh.vertices = new[]
                {
                    new Vector3(-0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f),
                    new Vector3(0.5f, 0.5f, 0f), new Vector3(0.5f, -0.5f, 0f)
                };
                board_mesh.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
                board_mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
                board_mesh.RecalculateBounds();
                board.GetComponent<MeshFilter>().sharedMesh = board_mesh;
                var shader = Shader.Find("Unlit/Color");
                if (shader == null) throw new InvalidOperationException("Unlit/Color が見つかりません。");
                board_material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                var board_renderer = board.GetComponent<MeshRenderer>();
                board_renderer.sharedMaterial = board_material;
                configure_renderer(board_renderer);
                board.transform.position = new Vector3(0f, 0f, 1f);

                var text_object = create_object("guide_text", typeof(RectTransform));
                text_mesh = text_object.AddComponent<TextMeshPro>();
                text_mesh.isOrthographic = true;
                text_mesh.enableAutoSizing = false;
                text_mesh.enableWordWrapping = true;
                text_mesh.richText = false;
                text_mesh.overflowMode = TextOverflowModes.Truncate;
                text_mesh.rectTransform.pivot = new Vector2(0f, 1f);
                configure_renderer(text_mesh.GetComponent<MeshRenderer>());
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        private GameObject create_object(string object_name, params Type[] components)
        {
            var instance = new GameObject(object_name, components) { hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(instance, preview_scene);
            return instance;
        }

        private static void configure_renderer(Renderer renderer)
        {
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        public void render(guide_board_settings settings)
        {
            settings.normalize();
            if (settings.font == null) throw new InvalidOperationException("TMP フォントを選択してください。");
            if (settings.font.material == null) throw new InvalidOperationException("フォントのマテリアルがありません。");
            if (render_texture == null || render_texture.width != settings.width || render_texture.height != settings.height)
            {
                release_texture();
                render_texture = new RenderTexture(settings.width, settings.height, 16, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
                {
                    name = "guide_preview_texture",
                    hideFlags = HideFlags.HideAndDontSave,
                    antiAliasing = 1,
                    useMipMap = false,
                    autoGenerateMips = false,
                    filterMode = FilterMode.Bilinear
                };
                render_texture.Create();
            }

            if (current_font != settings.font)
            {
                text_mesh.font = settings.font;
                text_mesh.fontSharedMaterial = settings.font.material;
                if (text_material != null) UnityEngine.Object.DestroyImmediate(text_material);
                text_material = new Material(settings.font.material) { hideFlags = HideFlags.HideAndDontSave };
                text_material.SetColor(ShaderUtilities.ID_FaceColor, Color.white);
                text_mesh.fontSharedMaterial = text_material;
                current_font = settings.font;
            }

            board.transform.localScale = new Vector3(settings.width, settings.height, 1f);
            board_material.color = settings.background_color;
            text_mesh.rectTransform.position = new Vector3(-settings.width * 0.5f + settings.padding, settings.height * 0.5f - settings.padding, 0f);
            text_mesh.rectTransform.sizeDelta = new Vector2(settings.width - settings.padding * 2f, settings.height - settings.padding * 2f);
            text_mesh.alignment = settings.alignment == 1 ? TextAlignmentOptions.Top : settings.alignment == 2 ? TextAlignmentOptions.TopRight : TextAlignmentOptions.TopLeft;
            text_mesh.fontSize = settings.font_size;
            text_mesh.color = settings.text_color;
            text_mesh.text = settings.text ?? string.Empty;
            text_mesh.ForceMeshUpdate();
            text_overflows = text_mesh.isTextOverflowing || text_mesh.isTextTruncated;
            string visible_characters = new string(text_mesh.text.Where(c => !char.IsWhiteSpace(c)).ToArray());
            has_missing_characters = !settings.font.HasCharacters(visible_characters, out uint[] missing_characters, true, false);
            preview_camera.backgroundColor = settings.background_color;
            preview_camera.orthographicSize = settings.height * 0.5f;
            preview_camera.aspect = (float)settings.width / settings.height;
            preview_camera.targetTexture = render_texture;
            var previous_active = RenderTexture.active;
            try
            {
                preview_camera.Render();
                render_count++;
            }
            finally
            {
                RenderTexture.active = previous_active;
            }
        }

        public byte[] encode_png()
        {
            if (render_texture == null) throw new InvalidOperationException("プレビューを先に生成してください。");
            var previous_active = RenderTexture.active;
            Texture2D readback = null;
            try
            {
                RenderTexture.active = render_texture;
                readback = new Texture2D(render_texture.width, render_texture.height, TextureFormat.RGB24, false, false);
                readback.ReadPixels(new Rect(0, 0, render_texture.width, render_texture.height), 0, 0, false);
                readback.Apply(false, false);
                return readback.EncodeToPNG();
            }
            finally
            {
                RenderTexture.active = previous_active;
                if (readback != null) UnityEngine.Object.DestroyImmediate(readback);
            }
        }

        private void release_texture()
        {
            if (preview_camera != null) preview_camera.targetTexture = null;
            if (render_texture == null) return;
            if (RenderTexture.active == render_texture) RenderTexture.active = null;
            render_texture.Release();
            UnityEngine.Object.DestroyImmediate(render_texture);
            render_texture = null;
        }

        public void Dispose()
        {
            release_texture();
            if (preview_scene.IsValid()) EditorSceneManager.ClosePreviewScene(preview_scene);
            preview_scene = default;
            if (text_material != null) UnityEngine.Object.DestroyImmediate(text_material);
            if (board_material != null) UnityEngine.Object.DestroyImmediate(board_material);
            if (board_mesh != null) UnityEngine.Object.DestroyImmediate(board_mesh);
            preview_camera = null;
            text_material = null;
            board_material = null;
            board_mesh = null;
        }
    }
}
