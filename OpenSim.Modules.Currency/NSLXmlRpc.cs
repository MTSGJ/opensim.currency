/* 
 * Copyright (c) Contributors, http://www.nsl.tuis.ac.jp
 *
 */


using System;
using System.Collections;
using System.IO;
using System.Xml;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

using log4net;
using Nwc.XmlRpc;

using System.Net.Security;
using NSL.Certificate.Tools;


namespace NSL.Network.XmlRpc 
{
    public class NSLXmlRpcRequest : XmlRpcRequest
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private Encoding _encoding = new UTF8Encoding();
        private XmlRpcRequestSerializer _serializer = new XmlRpcRequestSerializer();
        private XmlRpcResponseDeserializer _deserializer = new XmlRpcResponseDeserializer();


        public NSLXmlRpcRequest()
        {
            _params = new ArrayList();
        }


        public NSLXmlRpcRequest(String methodName, IList parameters)
        {
            MethodName = methodName;
            _params = parameters;
        }


        //public XmlRpcResponse certSend(String url, X509Certificate2 myClientCert, bool checkServerCert, Int32 timeout)
        public XmlRpcResponse certSend(String url, NSLCertificateVerify certVerify, bool checkServerCert, Int32 timeout)
        {
            m_log.InfoFormat("[MONEY NSL XMLRPC]: XmlRpcResponse certSend: connect to {0}", url);

#pragma warning disable SYSLIB0014  // Use HttpClient instead of WebRequest.Create.
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
#pragma warning restore SYSLIB0014
            if (request==null) {
                throw new XmlRpcException(XmlRpcErrorCodes.TRANSPORT_ERROR, XmlRpcErrorCodes.TRANSPORT_ERROR_MSG +": Could not create request with " + url);
            }

            X509Certificate2 clientCert = null;

            request.Method = "POST";
            request.ContentType = "text/xml";
            request.AllowWriteStreamBuffering = true;
            request.Timeout = timeout;
            request.UserAgent = "NSLXmlRpcRequest";

            if (certVerify != null) {
                clientCert = certVerify.GetPrivateCert();
                if (clientCert != null) request.ClientCertificates.Add(clientCert);  // Own certificate   // 自身の証明書
                request.ServerCertificateValidationCallback = new RemoteCertificateValidationCallback(certVerify.ValidateServerCertificate);
            }
            else {
                checkServerCert = false;
            }
            //
            if (!checkServerCert) {
                request.Headers.Add("NoVerifyCert", "true");   // Do not verify the certificate of the other party  // 相手の証明書を検証しない
            }

            //
            Stream stream = null;
            try { 
                stream = request.GetRequestStream();
            }
#pragma warning disable CS0168
            catch (Exception ex) {
#pragma warning restore CS0168
                m_log.ErrorFormat("[MONEY NSL XMLRPC]: GetRequestStream Error: {0}", ex);
                stream = null;
            }
            if (stream==null) return null;

            //
            XmlTextWriter xml = new XmlTextWriter(stream, _encoding);
            _serializer.Serialize(xml, this);
            xml.Flush();
            xml.Close();

            HttpWebResponse response = null;
            try { 
                response = (HttpWebResponse)request.GetResponse();
            }
            catch (Exception ex) {
                m_log.ErrorFormat("[MONEY NSL XMLRPC]: XmlRpcResponse certSend: GetResponse Error: {0}", ex.ToString());
                return null;
            }
            StreamReader input = new StreamReader(response.GetResponseStream());

            string inputXml = input.ReadToEnd();
            XmlRpcResponse resp = (XmlRpcResponse)_deserializer.Deserialize(inputXml);

            input.Close();
            response.Close();
            return resp;
        }


        public async Task<XmlRpcResponse> certSendAsync(string url, NSLCertificateVerify certVerify, bool checkServerCert, int timeout)
        {
            m_log.InfoFormat("[MONEY NSL XMLRPC]: XmlRpcResponse certSendAsync: connect to {0}", url);

            X509Certificate2 clientCert = null;

            // ハンドラ設定
            var handler = new HttpClientHandler
            {
                ClientCertificateOptions = ClientCertificateOption.Manual,
            };

            if (certVerify != null)
            {
                clientCert = certVerify.GetPrivateCert();
                if (clientCert != null)
                {
                    handler.ClientCertificates.Add(clientCert);
                }

                handler.ServerCertificateCustomValidationCallback = certVerify.ValidateServerCertificate;
            }
            else if (!checkServerCert)
            {
                handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            }

            using var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromMilliseconds(timeout)
            };

            // XML-RPC のシリアライズ
            string requestXml;
            using (var stringWriter = new StringWriter())
            {
                using var xmlWriter = new XmlTextWriter(stringWriter) { Formatting = Formatting.Indented };
                _serializer.Serialize(xmlWriter, this);
                xmlWriter.Flush();
                requestXml = stringWriter.ToString();
            }

            // リクエストボディの作成
            var content = new StringContent(requestXml, _encoding, "text/xml");
            content.Headers.Add("User-Agent", "NSLXmlRpcRequest");

            if (!checkServerCert)
            {
                content.Headers.Add("NoVerifyCert", "true");
            }

            // リクエスト送信
            HttpResponseMessage response;
            try
            {
                response = await httpClient.PostAsync(url, content);
            }
            catch (Exception ex)
            {
                m_log.ErrorFormat("[MONEY NSL XMLRPC]: XmlRpcResponse certSendAsync: PostAsync Error: {0}", ex.ToString());
                return null;
            }

            // レスポンス取得
            string responseXml = await response.Content.ReadAsStringAsync();

            return (XmlRpcResponse)_deserializer.Deserialize(responseXml);
        }
    }
}
