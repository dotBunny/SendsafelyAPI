using System;
using System.Collections.Generic;
using System.Text;
using SendSafely.Exceptions;
using System.IO;
using SendSafely.Objects;

namespace SendSafely.Utilities
{
    class DownloadFileUtility
    {
        public static int BufferSize = 4096;
        
        private PackageInformation pkgInfo;
        private Directory directoryInfo;
        private ISendSafelyProgress progress;
        private Connection connection;
        private String downloadAPI;
        private String password;

        public DownloadFileUtility(Connection connection, PackageInformation pkgInfo, ISendSafelyProgress progress, String downloadAPI, String password)
        {
            this.pkgInfo = pkgInfo;
            this.progress = progress;
            this.connection = connection;
            this.downloadAPI = downloadAPI;
            this.password = password;
        }

        public DownloadFileUtility(Connection connection, Directory directory, PackageInformation pkgInfo, ISendSafelyProgress progress, String downloadAPI)
        {
            this.pkgInfo = pkgInfo;
            this.progress = progress;
            this.connection = connection;
            this.downloadAPI = downloadAPI;
            this.directoryInfo = directory;
        }

        public FileInfo downloadFile(String fileId)
        {
            File fileToDownload = findFile(fileId);

            FileInfo newFile = createTempFile(fileToDownload);

            Endpoint p = createEndpoint(pkgInfo, fileId);
            
            string cachedChecksum = CryptUtility.pbkdf2(pkgInfo.KeyCode, pkgInfo.PackageCode, 1024);
            
            using (FileStream decryptedFileStream = newFile.OpenWrite())
            {
                for (int i = 1; i <= fileToDownload.Parts; i++)
                {
                    // Reserve in ~3mb blocks
                    MemoryStream memoryStream = new MemoryStream(3072000);
                    using (ProgressStream progressStream = new ProgressStream(memoryStream, progress, "Downloading", fileToDownload.FileSize, 0))
                    {
                        DownloadSegment(progressStream, p, i, cachedChecksum);
                    }
                    memoryStream.Seek(0, SeekOrigin.Begin);
                    CryptUtility.DecryptFile(decryptedFileStream, memoryStream, getDecryptionKey());
                    memoryStream.Close();
                }
            }

            return newFile;
        }

        private Endpoint createEndpoint(PackageInformation pkgInfo, String fileId)
        {
            Endpoint p;
            if (this.directoryInfo != null)
            {
                p = ConnectionStrings.Endpoints["downloadFileFromDirectory"].Clone();
                p.Path = p.Path.Replace("{packageId}", pkgInfo.PackageId);
                p.Path = p.Path.Replace("{fileId}", fileId);
                p.Path = p.Path.Replace("{directoryId}", directoryInfo.DirectoryId);
            } else
            {
                p = ConnectionStrings.Endpoints["downloadFile"].Clone();
                p.Path = p.Path.Replace("{packageId}", pkgInfo.PackageId);
                p.Path = p.Path.Replace("{fileId}", fileId);
            }

            return p;
        }
        

        private void DownloadSegment(Stream progressStream, Endpoint p, int part, string cachedChecksum = null)
        {
            DownloadFileRequest request = new DownloadFileRequest
            {
                Api = this.downloadAPI,
                Checksum = cachedChecksum ?? createChecksum(),
                Part = part
            };
            
            if (this.password != null) {
                request.Password = this.password;
            }

            using (Stream objStream = connection.CallServer(p, request))
            {
                byte[] tmp = new byte[BufferSize];
                int l;
                while ((l = objStream.Read(tmp, 0, BufferSize)) != 0)
                {
                    progressStream.Write(tmp, 0, l);
                }
            }
        }

        private char[] getDecryptionKey()
        {
            String keyString = pkgInfo.ServerSecret + pkgInfo.KeyCode;
            return keyString.ToCharArray();
        }

        private String createChecksum()
        {
            return CryptUtility.pbkdf2(pkgInfo.KeyCode, pkgInfo.PackageCode, 1024);
        }

        private File findFile(String fileId)
        {
            foreach (File f in pkgInfo.Files)
            {
                if (f.FileId.Equals(fileId))
                {
                    return f;
                }
            }

            if (this.directoryInfo != null)
            { 
                foreach (FileResponse f in directoryInfo.Files)
                {
                    if (f.FileId.Equals(fileId))
                    {
                        return new File(f.FileId, f.FileName, f.FileSize, f.Parts);
                    }
                }
            }
            throw new FileDownloadException("Failed to find the file");
        }

        private FileInfo createTempFile(File file)
	    {
            return createTempFile(Guid.NewGuid().ToString());
	    }

        private FileInfo createTempFile()
        {
            return createTempFile(Guid.NewGuid().ToString());
        }

        private FileInfo createTempFile(String fileName)
        {
            return new FileInfo(System.IO.Path.GetTempPath() + fileName);
        }
    }
}
