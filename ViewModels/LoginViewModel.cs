using System.ComponentModel.DataAnnotations;
namespace ClinicaVeterinaria.ViewModels;
public class LoginViewModel
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;
    [Required, DataType(DataType.Password)]
    public string Senha { get; set; } = string.Empty;
}