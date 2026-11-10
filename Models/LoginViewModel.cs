using System.ComponentModel.DataAnnotations;

namespace CapstoneDesign.Models
{
    public class LoginViewModel
    {
        private string username;
        private string password;

        [Required(ErrorMessage = "Please enter your username or email")]
        public string UserName
        {
            get { return username; }
            set { username = value; }
        }

        [Required(ErrorMessage = "Please enter your password")]
        public string Password
        {
            get { return password; }
            set { password = value; }
        }

    }
}
