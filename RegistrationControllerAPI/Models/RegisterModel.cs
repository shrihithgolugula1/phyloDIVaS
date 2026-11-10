namespace RegistrationControllerAPI.Models
{
    public class RegisterModel
    {
        //Fields needed for register
        public string username;
        public string email;
        public int age;
        public string currentStatus;
        public string highestDegree;
        public string institution;
        public string stateOrCountry;

        //Constructor with everything needed
        public RegisterModel(string username, string email, int age, string currentStatus, string highestDegree, string institution, string stateOrCountry)
        {
            this.username = username;
            this.email = email;
            this.age = age;
            this.currentStatus = currentStatus;
            this.highestDegree = highestDegree;
            this.institution = institution;
            this.stateOrCountry = stateOrCountry;
        }

        //Getters and setters

        public string Username
        {
            get{ return username; }
            set{ username = value; }
        }

        public string Email
        {
            get { return email; }
            set { email = value; }
        }

        public int Age
        {
            get { return age; }
            set { age = value; }
        }

        public string CurrentStatus
        {
            get { return currentStatus; }
            set { currentStatus = value; }
        }

        public string HighestDegree
        {
            get { return highestDegree; }
            set { highestDegree = value; }
        }

        public string Institution
        {
            get { return institution; }
            set { institution = value; }
        }

        public string StateOrCountry
        { 
            get { return stateOrCountry; } 
            set { stateOrCountry = value; } 
        }
    }
}
