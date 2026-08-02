namespace IRSpeedyVPN.WebServices
{

        public class DefaultEncryptedResponse<T>
        {
            public string data { get; set; }
            public string message { get; set; }

        public T Decrypted {
            get
            {
                return new NewServiceDecryptor().DecryptFromBase64<T>(data);
            }
        }
        public string DecryptedString
        {
            get
            {
                return new NewServiceDecryptor().DecryptFromBase64String(data);
            }
        }

    }


    
}
