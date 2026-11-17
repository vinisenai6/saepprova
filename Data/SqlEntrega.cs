using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ClinicaVeterinaria.Services;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ClinicaVeterinaria.Data;

public static class SqlEntrega
{
    public static async Task ExportarAsync(IServiceProvider services, string arquivo, string banco)
    {
        if (!Regex.IsMatch(banco, @"^[A-Za-z][A-Za-z0-9_]*$"))
            throw new ArgumentException("Nome de banco inválido.");
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var usuarios = await db.Usuarios.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
        var tutores = await db.Tutores.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
        var pets = await db.Pets.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
        var agendas = await db.Agendamentos.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
        if (new[] { usuarios.Count, tutores.Count, pets.Count, agendas.Count }.Any(n => n < 3))
            throw new InvalidOperationException("Cadastre pelo menos três registros em cada tabela de negócio antes de exportar.");
        var sql = new StringBuilder($"-- SAEP: criação e população. Preserve a chave AES usada nesta carga.\nUSE [master];\nGO\nIF DB_ID(N'{banco}') IS NULL CREATE DATABASE [{banco}];\nGO\nUSE [{banco}];\nGO\n");
        sql.Append(db.GetService<IMigrator>().GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent));
        sql.AppendLine("\nSET XACT_ABORT ON;\nBEGIN TRANSACTION;");
        void Tabela(string nome, string colunas, IEnumerable<string> valores)
        {
            sql.AppendLine($"SET IDENTITY_INSERT [{nome}] ON;");
            foreach (var linha in valores)
            {
                var id = linha.Split(',')[0];
                sql.AppendLine($"IF NOT EXISTS (SELECT 1 FROM [{nome}] WHERE [Id] = {id}) INSERT INTO [{nome}] ({colunas}) VALUES ({linha});");
            }
            sql.AppendLine($"SET IDENTITY_INSERT [{nome}] OFF;");
        }
        Tabela("Usuarios", "[Id],[Nome],[Email],[SenhaHash],[Perfil]",
            usuarios.Select(x => $"{x.Id},{S(x.Nome)},{S(x.Email)},{S(x.SenhaHash)},{S(x.Perfil)}"));
        Tabela("Tutores", "[Id],[Nome],[CpfCriptografado],[Telefone],[Email]",
            tutores.Select(x => $"{x.Id},{S(x.Nome)},{S(x.CpfCriptografado)},{S(x.Telefone)},{S(x.Email)}"));
        Tabela("Pets", "[Id],[Nome],[Especie],[Raca],[DataNascimento],[TutorId]",
            pets.Select(x => $"{x.Id},{S(x.Nome)},{S(x.Especie)},{S(x.Raca)},{D(x.DataNascimento)},{x.TutorId}"));
        Tabela("Agendamentos", "[Id],[DataHora],[Motivo],[Status],[TutorId],[PetId]",
            agendas.Select(x => $"{x.Id},{D(x.DataHora)},{S(x.Motivo)},{S(x.Status)},{x.TutorId},{x.PetId}"));
        sql.AppendLine("COMMIT;\nGO");
        sql.AppendLine("SELECT 'Usuarios' AS Tabela, COUNT(*) AS Quantidade FROM Usuarios UNION ALL SELECT 'Tutores', COUNT(*) FROM Tutores UNION ALL SELECT 'Pets', COUNT(*) FROM Pets UNION ALL SELECT 'Agendamentos', COUNT(*) FROM Agendamentos;\nGO");
        await File.WriteAllTextAsync(arquivo, sql.ToString(), Encoding.UTF8);
        Console.WriteLine($"SQL completo salvo em: {Path.GetFullPath(arquivo)}");
    }
    private static string S(string? valor) => valor is null ? "NULL" : "N'" + valor.Replace("'", "''") + "'";
    private static string D(DateTime? valor) => valor.HasValue ? "'" + valor.Value.ToString("yyyy-MM-ddTHH:mm:ss.fffffff", CultureInfo.InvariantCulture) + "'" : "NULL";

    // Apoio à validação do material pelo professor; não é uma tela do sistema.
    public static async Task ImportarAsync(IServiceProvider services, string arquivo)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cs = new SqlConnectionStringBuilder(db.Database.GetConnectionString()) { InitialCatalog = "master" };
        using var connection = new SqlConnection(cs.ConnectionString);
        await connection.OpenAsync();
        foreach (var batch in Regex.Split(await File.ReadAllTextAsync(arquivo), @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(batch)) continue;
            using var command = new SqlCommand(batch, connection) { CommandTimeout = 60 };
            await command.ExecuteNonQueryAsync();
        }
        Console.WriteLine("SQL executado sem inicializador automático.");
    }

    public static async Task VerificarAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var crypto = scope.ServiceProvider.GetRequiredService<CriptografiaService>();
        var counts = new[] { await db.Usuarios.CountAsync(), await db.Tutores.CountAsync(), await db.Pets.CountAsync(), await db.Agendamentos.CountAsync() };
        if (counts.Any(n => n < 3)) throw new Exception("Carga incompleta.");
        foreach (var t in await db.Tutores.AsNoTracking().ToListAsync())
        {
            var cpf = crypto.Descriptografar(t.CpfCriptografado);
            if (!Regex.IsMatch(cpf, @"^\d{11}$") || t.CpfCriptografado.Contains(cpf)) throw new Exception("CPF não protegido.");
        }
        var admin = await db.Usuarios.SingleAsync(u => u.Email == "admin@clinica.com");
        if (!BCrypt.Net.BCrypt.Verify("Admin@123", admin.SenhaHash)) throw new Exception("Hash do administrador inválido.");
        if (await db.Agendamentos.Include(a => a.Pet).AnyAsync(a => a.TutorId != a.Pet.TutorId)) throw new Exception("Relação inconsistente.");
        Console.WriteLine($"PASSOU: Usuarios={counts[0]}, Tutores={counts[1]}, Pets={counts[2]}, Agendamentos={counts[3]}; CPF AES-GCM, senha BCrypt e relações conferidas.");
    }
}