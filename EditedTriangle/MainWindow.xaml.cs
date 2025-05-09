using OpenTK;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms.Integration;
using System.Windows.Media;

namespace EditedTriangle
{
    public partial class MainWindow : Window
    {
        private List<Vector3> vertices = new List<Vector3>();
        private List<int> indices = new List<int>();
        private float rotationAngle = 0f;

        // Shadow mapping
        private int shadowMapFBO;
        private int shadowMapTexture;
        private readonly int shadowWidth = 2048;
        private readonly int shadowHeight = 2049;

        // Shader programs
        private int mainShaderProgram;
        private int shadowShaderProgram;

        // Buffers
        private int vao, vbo, ebo;
        private int indexCount;

        private Vector3 modelCenter = Vector3.Zero;
        private float modelScale = 1.0f;

        // Путь к OBJ
        private readonly string objPath = @"M:\комната.obj";//@"M:\untitled.obj";//@"M:\untitled22.obj";//

        //[STAThread]
        //public static void Main()
        //{
        //    var app = new Application();
        //    app.Run(new MainWindow());
        //}
        public MainWindow()
        {
            InitializeComponent();
            glControl.Load += GlControl_Load;
            glControl.Paint += GlControl_Paint;
            CompositionTarget.Rendering += CompositionTarget_Rendering;
        }

        private void GlControl_Load(object sender, EventArgs e)
        {
            // 1) Общие настройки
            GL.ClearColor(Color4.LightGray);
            GL.Enable(EnableCap.DepthTest);

            // 2) Компилируем шейдеры
            mainShaderProgram = CreateShaderProgram(mainVertSource, mainFragSource);
            shadowShaderProgram = CreateShaderProgram(shadowVertSource, shadowFragSource);

            // 3) Генерация VAO / VBO / EBO
            GL.GenVertexArrays(1, out vao);
            GL.GenBuffers(1, out vbo);
            GL.GenBuffers(1, out ebo);

            GL.BindVertexArray(vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, ebo);

            // 4) Загружаем модель в буферы
            LoadOBJ(objPath);

            // 5) Настройка атрибута позиции
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 0, 0);
            GL.EnableVertexAttribArray(0);

            GL.BindVertexArray(0);

            // 6) Настройка теневой карты
            GL.GenFramebuffers(1, out shadowMapFBO);
            GL.GenTextures(1, out shadowMapTexture);
            GL.BindTexture(TextureTarget.Texture2D, shadowMapTexture);
            GL.TexImage2D(TextureTarget.Texture2D, 0,
                          PixelInternalFormat.DepthComponent,
                          shadowWidth, shadowHeight, 0,
                          OpenTK.Graphics.OpenGL.PixelFormat.DepthComponent,
                          PixelType.Float, IntPtr.Zero);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToBorder);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToBorder);
            float[] border = { 1f, 1f, 1f, 1f };
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureBorderColor, border);

            GL.BindFramebuffer(FramebufferTarget.Framebuffer, shadowMapFBO);
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment,
                                    TextureTarget.Texture2D, shadowMapTexture, 0);
            GL.DrawBuffer(DrawBufferMode.None);
            GL.ReadBuffer(ReadBufferMode.None);
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

            // Настройки текстур для улучшения качества
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToBorder);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToBorder);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureCompareMode, (int)TextureCompareMode.CompareRefToTexture);

            // Включение коррекции глубины
            GL.Enable(EnableCap.PolygonOffsetFill);
            GL.PolygonOffset(0f, 0f);
        }

        private void LoadOBJ(string filePath)
        {
            vertices.Clear();
            indices.Clear();
            Matrix4 rotation = Matrix4.CreateRotationX(MathHelper.DegreesToRadians(0f));

            foreach (var line in File.ReadAllLines(filePath))
            {
                var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) continue;


                if (parts[0] == "v")
                {
                    float x = float.Parse(parts[1], CultureInfo.InvariantCulture);
                    float y = float.Parse(parts[2], CultureInfo.InvariantCulture);
                    float z = float.Parse(parts[3], CultureInfo.InvariantCulture);
                    Vector4 originalPosition = new Vector4(x, y, z, 1.0f);
                    Vector4 rotatedPosition = Vector4.Transform(originalPosition, rotation);

                    vertices.Add(new Vector3(rotatedPosition.X, rotatedPosition.Y, rotatedPosition.Z));
                }
                else if (parts[0] == "f")
                {
                    // Обработка треугольных и четырехугольных граней
                    var faceVertices = new List<int>();
                    for (int i = 1; i < parts.Length; i++)
                    {
                        var vertexData = parts[i].Split('/');
                        int vertexIndex = int.Parse(vertexData[0], CultureInfo.InvariantCulture) - 1;
                        faceVertices.Add(vertexIndex);
                    }

                    // Триангуляция полигона (если это не треугольник)
                    for (int i = 1; i < faceVertices.Count - 1; i++)
                    {
                        indices.Add(faceVertices[0]);
                        indices.Add(faceVertices[i]);
                        indices.Add(faceVertices[i + 1]);
                    }
                }
            }

            // Центруем и масштабируем
            CenterModel();
            indexCount = indices.Count;

            // Заливка в буферы
            GL.BufferData(BufferTarget.ArrayBuffer,
                          vertices.Count * Vector3.SizeInBytes,
                          vertices.ToArray(),
                          BufferUsageHint.StaticDraw);

            GL.BufferData(BufferTarget.ElementArrayBuffer,
                          indexCount * sizeof(int),
                          indices.ToArray(),
                          BufferUsageHint.StaticDraw);
        }

        private void CenterModel()
        {
            if (vertices.Count == 0) return;

            Vector3 min = new Vector3(float.MaxValue);
            Vector3 max = new Vector3(float.MinValue);

            foreach (var v in vertices)
            {
                min = Vector3.ComponentMin(min, v);
                max = Vector3.ComponentMax(max, v);
            }

            modelCenter = (min + max) / 2f;
            float size = Math.Max(max.X - min.X,
                           Math.Max(max.Y - min.Y, max.Z - min.Z));
            modelScale = 30f / size;
        }

        private void GlControl_Paint(object sender, EventArgs e)
        {
            // Shadow pass
            GL.Viewport(0, 0, shadowWidth, shadowHeight);
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, shadowMapFBO);
            GL.Clear(ClearBufferMask.DepthBufferBit);
            GL.UseProgram(shadowShaderProgram);

            var near = 1f;
            var far = 100f;
            float orthoSize = 50f; // Экспериментируйте с этим значением
            var lightProj = Matrix4.CreateOrthographicOffCenter(
                -orthoSize,
                orthoSize,
                -orthoSize,
                orthoSize,
                near,
                far + 100f); // Увеличим far plane
            var lightPos = new Vector3(0, 60, 60);
            var lightView = Matrix4.LookAt(lightPos, modelCenter, Vector3.UnitY);
            var lightSpace =  lightView * lightProj;

            var modelMat = Matrix4.CreateScale(modelScale)
                         * Matrix4.CreateRotationY(MathHelper.DegreesToRadians(rotationAngle))
                         * Matrix4.CreateTranslation(-modelCenter);

            GL.UniformMatrix4(GL.GetUniformLocation(shadowShaderProgram, "uLightSpaceMatrix"), false, ref lightSpace);
            GL.UniformMatrix4(GL.GetUniformLocation(shadowShaderProgram, "uModel"), false, ref modelMat);

            GL.BindVertexArray(vao);
            GL.DrawElements(PrimitiveType.Triangles, indexCount, DrawElementsType.UnsignedInt, 0);

            // Main pass
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            GL.Viewport(0, 0, glControl.Width, glControl.Height);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            GL.UseProgram(mainShaderProgram);

            var proj = Matrix4.CreatePerspectiveFieldOfView(MathHelper.PiOver4,
                        (float)glControl.Width / glControl.Height,
                        1f, 1000f);
            var view = Matrix4.LookAt(new Vector3(0, 0, 70), Vector3.Zero, Vector3.UnitY);

            GL.UniformMatrix4(GL.GetUniformLocation(mainShaderProgram, "uProjection"), false, ref proj);
            GL.UniformMatrix4(GL.GetUniformLocation(mainShaderProgram, "uView"), false, ref view);
            GL.UniformMatrix4(GL.GetUniformLocation(mainShaderProgram, "uModel"), false, ref modelMat);
            GL.UniformMatrix4(GL.GetUniformLocation(mainShaderProgram, "uLightSpaceMatrix"), false, ref lightSpace);
            GL.Uniform3(GL.GetUniformLocation(mainShaderProgram, "uLightPos"), lightPos);

            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, shadowMapTexture);
            GL.Uniform1(GL.GetUniformLocation(mainShaderProgram, "uShadowMap"), 0);

            GL.BindVertexArray(vao);
            GL.DrawElements(PrimitiveType.Triangles, indexCount, DrawElementsType.UnsignedInt, 0);

            glControl.SwapBuffers();
        }

        private void CompositionTarget_Rendering(object sender, EventArgs e)
        {
            rotationAngle += 0.1f;
            if (rotationAngle >= 360f) rotationAngle = 0f;
            glControl.Invalidate();
        }

        // ==== Шейдерные исходники ====

        private readonly string mainVertSource = @"#version 330 core
layout(location = 0) in vec3 aPosition;
uniform mat4 uProjection;
uniform mat4 uView;
uniform mat4 uModel;
uniform mat4 uLightSpaceMatrix;
out vec4 FragPosLightSpace;
void main() {
    gl_Position = uProjection * uView * uModel * vec4(aPosition, 1.0);
    FragPosLightSpace = uLightSpaceMatrix * uModel * vec4(aPosition, 1.0);
}";

        private readonly string mainFragSource = @"#version 330 core
in vec4 FragPosLightSpace;
out vec4 FragColor;
uniform sampler2D uShadowMap;
uniform vec3 uLightPos;
uniform vec3 uViewPos;

float ShadowCalculation(vec4 fragPosLightSpace, vec3 normal, vec3 lightDir) {
    // Преобразование координат в диапазон [0,1]
    vec3 projCoords = fragPosLightSpace.xyz / fragPosLightSpace.w;
    projCoords = projCoords * 0.5 + 0.5;
    
    // Ранний выход за пределами shadow map
    if(projCoords.z > 1.0 || projCoords.z < 0.0) 
        return 0.0;

    // Получение глубины из shadow map
    float closestDepth = texture(uShadowMap, projCoords.xy).r;
    float currentDepth = projCoords.z;

    // Адаптивное смещение на основе угла поверхности
    float bias = 0.005;//max(0.005 * (1.0 - dot(normal, lightDir)), 0.001);
    
    // Улучшенный PCF фильтр 5x5
    float shadow = 0.0;
    vec2 texelSize = 1.0 / textureSize(uShadowMap, 0);
    for(int x = -2; x <= 2; ++x) {
        for(int y = -2; y <= 2; ++y) {
            float depth = texture(uShadowMap, projCoords.xy + vec2(x,y)*texelSize).r; 
            shadow += (currentDepth - bias) > depth ? 1.0 : 0.0;        
        }    
    }
    shadow /= 25.0;

    // Плавное затухание на границах
    float borderFade = 0.02;
    vec2 uv = projCoords.xy;
    uv = uv * (1.0 - uv.yx); // Квадратичное затухание
    float fade = min(uv.x, uv.y) / borderFade;
    fade = clamp(fade, 0.0, 1.0);
    shadow *= fade;

    return shadow;
}

void main() {
    // Вычисление нормалей через производные
    vec3 worldPos = FragPosLightSpace.xyz;
    vec3 normal = normalize(cross(dFdx(worldPos), dFdy(worldPos)));
    
    // Направление к источнику света
    vec3 lightDir = normalize(uLightPos - worldPos);
    
    // Расчет тени
    float shadow = ShadowCalculation(FragPosLightSpace, normal, lightDir);
    
    // Базовый цвет и освещение
    vec3 color = vec3(1.0); // Белый цвет
    vec3 lighting = mix(color, color * 0.7, shadow);
    
    // Гамма-коррекция
    const float gamma = 2.2;
    FragColor = vec4(pow(lighting, vec3(1.0/gamma)), 1.0);
}";

        private readonly string shadowVertSource = @"#version 330 core
layout(location = 0) in vec3 aPosition;
uniform mat4 uLightSpaceMatrix;
uniform mat4 uModel;
void main() {
    gl_Position = uLightSpaceMatrix * uModel * vec4(aPosition, 1.0);
}";

        private readonly string shadowFragSource = @"#version 330 core
void main() { }";

        private int CreateShaderProgram(string vertSrc, string fragSrc)
        {
            int vs = GL.CreateShader(ShaderType.VertexShader);
            GL.ShaderSource(vs, vertSrc);
            GL.CompileShader(vs);
            GL.GetShader(vs, ShaderParameter.CompileStatus, out int okV);
            if (okV == 0) throw new Exception($"Vertex shader error: {GL.GetShaderInfoLog(vs)}");

            int fs = GL.CreateShader(ShaderType.FragmentShader);
            GL.ShaderSource(fs, fragSrc);
            GL.CompileShader(fs);
            GL.GetShader(fs, ShaderParameter.CompileStatus, out int okF);
            if (okF == 0) throw new Exception($"Fragment shader error: {GL.GetShaderInfoLog(fs)}");

            int prog = GL.CreateProgram();
            GL.AttachShader(prog, vs);
            GL.AttachShader(prog, fs);
            GL.LinkProgram(prog);
            GL.GetProgram(prog, GetProgramParameterName.LinkStatus, out int okP);
            if (okP == 0) throw new Exception($"Program link error: {GL.GetProgramInfoLog(prog)}");

            GL.DeleteShader(vs);
            GL.DeleteShader(fs);
            return prog;
        }
    }
}
