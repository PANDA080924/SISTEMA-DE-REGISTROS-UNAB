using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using QRCoder;
using Supabase;
using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace SISTEMADEREGISTROSUNAB // <-- Verifica que coincida con el namespace de tu proyecto
{
    public partial class Form1 : Form
    {
        // CREDENCIALES DE SUPABASE
        private const string SUPABASE_URL = "https://xnumrsfknhxuorroykuq.supabase.co";
        private const string SUPABASE_KEY = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6InhudW1yc2Zrbmh4dW9ycm95a3VxIiwicm9sZSI6ImFub24iLCJpYXQiOjE3ODYzMTI4NTUsImV4cCI6MjEwMTg4ODg1NX0.9-mcWmlNeoRN8hnC6TQ51R4rooLnF_uFEd0ZfEfI9ZM";

        private Supabase.Client _supabase = null!;

        // Controles Izquierda (Generador QR)
        private PictureBox pbCodigoQR = null!;
        private ComboBox cmbPuerta = null!;
        private TextBox txtCodigoEstudiante = null!;
        private Button btnValidarGenerar = null!;
        private Label lblEstadoEstudiante = null!;
        private Label lblCodigoTexto = null!;
        private Label lblTemporizador = null!;
        private CheckBox chkDinamico = null!;
        private System.Windows.Forms.Timer timerRenovacion = null!;
        private int _segundosRestantes = 30;

        // Estado Validado
        private EstudianteModel? _estudianteValidado = null;
        private List<PuertaModel> _listaPuertas = new();

        // Controles Derecha (Monitor de Registros en Vivo)
        private DataGridView dgvRegistros = null!;
        private System.Windows.Forms.Timer timerRefrescoTabla = null!;

        public Form1()
        {
            InitializeComponent();
            ConfigurarInterfaz();
            this.Load += async (s, e) => await InicializarSupabaseYDatosAsync();
        }

        private async Task InicializarSupabaseYDatosAsync()
        {
            try
            {
                var options = new SupabaseOptions { AutoConnectRealtime = false };
                _supabase = new Supabase.Client(SUPABASE_URL, SUPABASE_KEY, options);
                await _supabase.InitializeAsync();

                await CargarPuertasDesdeBDAsync();
                await CargarRegistrosEnTablaAsync();

                // Iniciar refresco automático de la tabla cada 5 segundos
                timerRefrescoTabla.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error conectando con Supabase: {ex.Message}", "Error de Conexión", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async Task CargarPuertasDesdeBDAsync()
        {
            var resp = await _supabase.From<PuertaModel>().Where(p => p.Activa == true).Get();
            _listaPuertas = resp.Models;

            cmbPuerta.Items.Clear();
            foreach (var p in _listaPuertas)
            {
                cmbPuerta.Items.Add($"{p.CodigoPuerta} - {p.NombrePuerta}");
            }

            if (cmbPuerta.Items.Count > 0)
            {
                cmbPuerta.SelectedIndex = 0;
            }
        }

        private async void BtnValidarGenerar_Click(object? sender, EventArgs e)
        {
            string codigo = txtCodigoEstudiante.Text.Trim();
            if (string.IsNullOrWhiteSpace(codigo))
            {
                MessageBox.Show("Ingresa un código de estudiante (ej. 2431113006).", "Campo requerido", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            btnValidarGenerar.Enabled = false;
            lblEstadoEstudiante.Text = "Verificando en base de datos...";
            lblEstadoEstudiante.ForeColor = Color.FromArgb(56, 189, 248);

            try
            {
                var res = await _supabase
                    .From<EstudianteModel>()
                    .Where(est => est.CodigoEstudiante == codigo)
                    .Get();

                var encontrado = res.Models.FirstOrDefault();

                if (encontrado == null)
                {
                    _estudianteValidado = null;
                    pbCodigoQR.Image = null;
                    lblCodigoTexto.Text = "CÓDIGO NO ENCONTRADO";
                    lblEstadoEstudiante.Text = $"✖ El código {codigo} no existe en la BD.";
                    lblEstadoEstudiante.ForeColor = Color.FromArgb(248, 113, 113);
                    return;
                }

                if (!encontrado.EstadoActivo)
                {
                    _estudianteValidado = null;
                    pbCodigoQR.Image = null;
                    lblEstadoEstudiante.Text = $"✖ Estudiante {encontrado.Nombres} está INACTIVO.";
                    lblEstadoEstudiante.ForeColor = Color.FromArgb(248, 113, 113);
                    return;
                }

                // Estudiante válido
                _estudianteValidado = encontrado;
                lblEstadoEstudiante.Text = $"✔ {encontrado.Apellidos}, {encontrado.Nombres}";
                lblEstadoEstudiante.ForeColor = Color.FromArgb(34, 197, 94);

                GenerarNuevoQR();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al consultar estudiante: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                btnValidarGenerar.Enabled = true;
            }
        }

        private void GenerarNuevoQR()
        {
            if (_estudianteValidado == null || cmbPuerta.SelectedIndex < 0) return;

            _segundosRestantes = 30;
            lblTemporizador.Text = "30s";

            string codigoPuerta = _listaPuertas[cmbPuerta.SelectedIndex].CodigoPuerta;
            string contenidoQR;

            if (chkDinamico.Checked)
            {
                long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                // Formato: UNAB | PUERTA | CODIGO_ESTUDIANTE | TIMESTAMP
                contenidoQR = $"UNAB|{codigoPuerta}|{_estudianteValidado.CodigoEstudiante}|{timestamp}";
            }
            else
            {
                contenidoQR = $"UNAB|{codigoPuerta}|{_estudianteValidado.CodigoEstudiante}";
            }

            lblCodigoTexto.Text = contenidoQR;

            using QRCodeGenerator qrGenerator = new QRCodeGenerator();
            using QRCodeData qrCodeData = qrGenerator.CreateQrCode(contenidoQR, QRCodeGenerator.ECCLevel.Q);
            using QRCode qrCode = new QRCode(qrCodeData);

            Bitmap qrImagen = qrCode.GetGraphic(20, Color.Black, Color.White, true);
            var imagenAnterior = pbCodigoQR.Image;
            pbCodigoQR.Image = qrImagen;
            imagenAnterior?.Dispose();
        }

        private async Task CargarRegistrosEnTablaAsync()
        {
            if (_supabase == null) return;

            try
            {
                var res = await _supabase
                    .From<RegistroAccesoModel>()
                    .Order(r => r.CreadoAt, Supabase.Postgrest.Constants.Ordering.Descending)
                    .Limit(30)
                    .Get();

                dgvRegistros.Rows.Clear();
                foreach (var reg in res.Models)
                {
                    dgvRegistros.Rows.Add(
                        reg.CreadoAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss"),
                        reg.TipoEvento,
                        reg.EscaneadoPor,
                        reg.CodigoEstudiante,
                        reg.Equipo
                    );
                }
            }
            catch
            {
                // Ignorar fallos silenciosos de red en el refresco automático
            }
        }

        private void TimerRenovacion_Tick(object? sender, EventArgs e)
        {
            if (!chkDinamico.Checked || _estudianteValidado == null) return;

            _segundosRestantes--;
            lblTemporizador.Text = $"{_segundosRestantes}s";

            if (_segundosRestantes <= 0)
            {
                GenerarNuevoQR();
            }
        }

        private void ConfigurarInterfaz()
        {
            this.Text = "Terminal de Control de Acceso y Monitoreo - UNAB";
            this.Size = new Size(1060, 680);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(15, 23, 42);
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;

            // ================= PANEL IZQUIERDO (GENERADOR QR) =================
            Panel pnlIzquierdo = new Panel
            {
                Location = new Point(15, 15),
                Size = new Size(420, 615),
                BackColor = Color.FromArgb(30, 41, 59)
            };

            Label lblTitulo = new Label
            {
                Text = "GENERADOR DE ACCESO UNAB",
                Font = new Font("Segoe UI", 14, FontStyle.Bold),
                ForeColor = Color.White,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Top,
                Height = 45
            };

            // Selector Puerta desde BD
            Label lblPuerta = new Label
            {
                Text = "1. Selecciona Puerta (Base de Datos):",
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(203, 213, 225),
                Location = new Point(25, 55),
                AutoSize = true
            };

            cmbPuerta = new ComboBox
            {
                Location = new Point(25, 78),
                Size = new Size(370, 28),
                Font = new Font("Segoe UI", 9.5f),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(15, 23, 42),
                ForeColor = Color.White
            };
            cmbPuerta.SelectedIndexChanged += (s, e) => GenerarNuevoQR();

            // Input Código de Estudiante
            Label lblCod = new Label
            {
                Text = "2. Código de Estudiante (ej. 2431113006):",
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(203, 213, 225),
                Location = new Point(25, 118),
                AutoSize = true
            };

            txtCodigoEstudiante = new TextBox
            {
                Location = new Point(25, 141),
                Size = new Size(235, 28),
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                BackColor = Color.FromArgb(15, 23, 42),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };

            btnValidarGenerar = new Button
            {
                Text = "Validar y Generar",
                Location = new Point(268, 140),
                Size = new Size(127, 29),
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                BackColor = Color.FromArgb(37, 99, 235),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnValidarGenerar.FlatAppearance.BorderSize = 0;
            btnValidarGenerar.Click += BtnValidarGenerar_Click;

            lblEstadoEstudiante = new Label
            {
                Text = "Ingresa tu código para validar matrícula...",
                Font = new Font("Segoe UI", 9f, FontStyle.Italic),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(25, 176),
                Size = new Size(370, 22)
            };

            // Contenedor QR
            Panel pnlContenedorQR = new Panel
            {
                Location = new Point(75, 208),
                Size = new Size(270, 270),
                BackColor = Color.White,
                Padding = new Padding(10)
            };

            pbCodigoQR = new PictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.Zoom
            };
            pnlContenedorQR.Controls.Add(pbCodigoQR);

            lblCodigoTexto = new Label
            {
                Text = "ESPERANDO VALIDACIÓN",
                Font = new Font("Consolas", 8.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(56, 189, 248),
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(15, 488),
                Size = new Size(390, 22)
            };

            chkDinamico = new CheckBox
            {
                Text = "Token dinámico (renovar cada 30s)",
                Font = new Font("Segoe UI", 9f),
                ForeColor = Color.FromArgb(203, 213, 225),
                Location = new Point(25, 520),
                AutoSize = true,
                Checked = true
            };
            chkDinamico.CheckedChanged += (s, e) =>
            {
                lblTemporizador.Visible = chkDinamico.Checked;
                GenerarNuevoQR();
            };

            lblTemporizador = new Label
            {
                Text = "30s",
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(34, 197, 94),
                Location = new Point(355, 522),
                AutoSize = true
            };

            Button btnActualizarQR = new Button
            {
                Text = "Regenerar Código QR",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                BackColor = Color.FromArgb(22, 163, 74),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(25, 555),
                Size = new Size(370, 38),
                Cursor = Cursors.Hand
            };
            btnActualizarQR.FlatAppearance.BorderSize = 0;
            btnActualizarQR.Click += (s, e) => GenerarNuevoQR();

            pnlIzquierdo.Controls.AddRange(new Control[]
            {
                lblTitulo, lblPuerta, cmbPuerta, lblCod, txtCodigoEstudiante,
                btnValidarGenerar, lblEstadoEstudiante, pnlContenedorQR,
                lblCodigoTexto, chkDinamico, lblTemporizador, btnActualizarQR
            });

            // ================= PANEL DERECHO (TABLA EN VIVO) =================
            Panel pnlDerecho = new Panel
            {
                Location = new Point(450, 15),
                Size = new Size(580, 615),
                BackColor = Color.FromArgb(30, 41, 59)
            };

            Label lblTituloTabla = new Label
            {
                Text = "REGISTROS DE ACCESO EN TIEMPO REAL",
                Font = new Font("Segoe UI", 13, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(20, 14),
                AutoSize = true
            };

            Button btnRefrescarTabla = new Button
            {
                Text = "↻ Actualizar",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                BackColor = Color.FromArgb(51, 65, 85),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(465, 12),
                Size = new Size(95, 28),
                Cursor = Cursors.Hand
            };
            btnRefrescarTabla.FlatAppearance.BorderSize = 0;
            btnRefrescarTabla.Click += async (s, e) => await CargarRegistrosEnTablaAsync();

            dgvRegistros = new DataGridView
            {
                Location = new Point(20, 52),
                Size = new Size(540, 540),
                BackgroundColor = Color.FromArgb(15, 23, 42),
                BorderStyle = BorderStyle.None,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                EnableHeadersVisualStyles = false,
                GridColor = Color.FromArgb(51, 65, 85)
            };

            dgvRegistros.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(37, 99, 235);
            dgvRegistros.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
            dgvRegistros.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            dgvRegistros.ColumnHeadersHeight = 36;

            dgvRegistros.DefaultCellStyle.BackColor = Color.FromArgb(15, 23, 42);
            dgvRegistros.DefaultCellStyle.ForeColor = Color.White;
            dgvRegistros.DefaultCellStyle.Font = new Font("Segoe UI", 9f);
            dgvRegistros.DefaultCellStyle.SelectionBackColor = Color.FromArgb(51, 65, 85);
            dgvRegistros.RowTemplate.Height = 32;

            dgvRegistros.Columns.Add("FechaHora", "Fecha y Hora");
            dgvRegistros.Columns.Add("Movimiento", "Evento");
            dgvRegistros.Columns.Add("Puerta", "Puerta / Garita");
            dgvRegistros.Columns.Add("CodigoEstudiante", "Cód. Estudiante");
            dgvRegistros.Columns.Add("Equipo", "Dispositivo");

            pnlDerecho.Controls.AddRange(new Control[] { lblTituloTabla, btnRefrescarTabla, dgvRegistros });

            this.Controls.Add(pnlIzquierdo);
            this.Controls.Add(pnlDerecho);

            // Timers
            timerRenovacion = new System.Windows.Forms.Timer { Interval = 1000 };
            timerRenovacion.Tick += TimerRenovacion_Tick;
            timerRenovacion.Start();

            timerRefrescoTabla = new System.Windows.Forms.Timer { Interval = 5000 };
            timerRefrescoTabla.Tick += async (s, e) => await CargarRegistrosEnTablaAsync();
        }
    }

    // ================= MODELOS DE SUPABASE PARA WINDOWS FORMS =================
    [Table("puertas")]
    public class PuertaModel : BaseModel
    {
        [PrimaryKey("id", false)]
        public string Id { get; set; } = string.Empty;

        [Column("codigo_puerta")]
        public string CodigoPuerta { get; set; } = string.Empty;

        [Column("nombre_puerta")]
        public string NombrePuerta { get; set; } = string.Empty;

        [Column("activa")]
        public bool Activa { get; set; }
    }

    [Table("estudiantes")]
    public class EstudianteModel : BaseModel
    {
        [PrimaryKey("id", false)]
        public string Id { get; set; } = string.Empty;

        [Column("codigo_estudiante")]
        public string CodigoEstudiante { get; set; } = string.Empty;

        [Column("nombres")]
        public string Nombres { get; set; } = string.Empty;

        [Column("apellidos")]
        public string Apellidos { get; set; } = string.Empty;

        [Column("estado_activo")]
        public bool EstadoActivo { get; set; }
    }

    [Table("registros_accesos")]
    public class RegistroAccesoModel : BaseModel
    {
        [PrimaryKey("id", false)]
        public long Id { get; set; }

        [Column("creado_at")]
        public DateTime CreadoAt { get; set; }

        [Column("codigo_estudiante")]
        public string CodigoEstudiante { get; set; } = string.Empty;

        [Column("equipo")]
        public string Equipo { get; set; } = string.Empty;

        [Column("tipo_evento")]
        public string TipoEvento { get; set; } = string.Empty;

        [Column("escaneado_por")]
        public string EscaneadoPor { get; set; } = string.Empty;

        [Column("numero_serie")]
        public string NumeroSerie { get; set; } = string.Empty;

        [Column("estudiante_id")]
        public string EstudianteId { get; set; } = string.Empty;

        [Column("dispositivo_id")]
        public string? DispositivoId { get; set; }
    }
}