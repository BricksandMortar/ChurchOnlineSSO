using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.UI;
using System.Web.UI.HtmlControls;
using System.Web.UI.WebControls;

using Newtonsoft.Json;

using Rock;
using Rock.Attribute;
using Rock.Communication;
using Rock.Data;
using Rock.Model;
using Rock.Security;
using Rock.Web.UI;
using Rock.Web.UI.Controls;

namespace RockWeb.Plugins.com_bricksandmortarstudio.Security
{
    /// <summary>
    /// Prompts user for login credentials.
    /// </summary>
    [DisplayName( "ChurchOnline SSO Login" )]
    [Category( "Bricks and Mortar Studio > Security" )]
    [Description( "Prompts user for login credentials." )]

    [LinkedPage( "New Account Page", "Page to navigate to when user selects 'Create New Account' (if blank will use 'NewAccountPage' page route)", false, "", "", 0 )]
    [LinkedPage( "Help Page", "Page to navigate to when user selects 'Help' option (if blank will use 'ForgotUserName' page route)", false, "", "", 1 )]
    [CodeEditorField( "Confirm Caption", "The text (HTML) to display when a user's account needs to be confirmed.", CodeEditorMode.Html, CodeEditorTheme.Rock, 100, false, @"
Thank-you for logging in, however, we need to confirm the email associated with this account belongs to you. We've sent you an email that contains a link for confirming.  Please click the link in your email to continue.
", "", 2 )]
    [LinkedPage( "Confirmation Page", "Page for user to confirm their account (if blank will use 'ConfirmAccount' page route)", false, "", "", 3 )]
    [SystemEmailField( "Confirm Account Template", "Confirm Account Email Template", false, Rock.SystemGuid.SystemEmail.SECURITY_CONFIRM_ACCOUNT, "", 4 )]
    [CodeEditorField( "Locked Out Caption", "The text (HTML) to display when a user's account has been locked.", CodeEditorMode.Html, CodeEditorTheme.Rock, 100, false, @"
Sorry, your account has been locked.  Please contact our office at {{ 'Global' | Attribute:'OrganizationPhone' }} or email {{ 'Global' | Attribute:'OrganizationEmail' }} to resolve this.  Thank-you. 
", "", 5 )]
    [BooleanField( "Hide New Account Option", "Should 'New Account' option be hidden?  For site's that require user to be in a role (Internal Rock Site for example), users shouldn't be able to create their own account.", false, "", 6, "HideNewAccount" )]
    [TextField( "New Account Text", "The text to show on the New Account button.", false, "Register", "", 7, "NewAccountButtonText" )]
    [RemoteAuthsField( "Remote Authorization Types", "Which of the active remote authorization types should be displayed as an option for user to use for authentication.", false, "", "", 8 )]
    [CodeEditorField( "Prompt Message", "Optional text (HTML) to display above username and password fields.", CodeEditorMode.Html, CodeEditorTheme.Rock, 100, false, @"", "", 9 )]
    [UrlLinkField( "Redirect URL", "URL to redirect user to upon successful login.  Example: http://online.rocksoliddemochurch.com", true, "", "", 10 )]
    [GroupTypeGroupField( "Check In Group", "The group online campus guests should check into. Your checkin in grouptype will need 'Display Options > Show in Group Lists' enabled", "Check In Group", true, order:11 )]
    [CampusField( "Online Campus Location", "The campus people will be checked into", true, "", "", 12 )]
    [SchedulesField( "Online Campus Schedules", "The schedules church online is available for", true, order: 13 )]
    [TextField( "Church Online SSO Key", "Your SSO key from church online", true, isPassword: true, key: "SSOKey", order: 14 )]
    public partial class ChurchOnlineLogin : RockBlock
    {
        #region Base Control Methods

        /// <summary>
        /// Raises the <see cref="E:System.Web.UI.Control.Init" /> event.
        /// </summary>
        /// <param name="e">An <see cref="T:System.EventArgs" /> object that contains the event data.</param>
        protected override void OnInit( EventArgs e )
        {
            base.OnInit( e );

            btnNewAccount.Visible = !GetAttributeValue( "HideNewAccount" ).AsBoolean();
            btnNewAccount.Text = this.GetAttributeValue( "NewAccountButtonText" ) ?? "Register";

            phExternalLogins.Controls.Clear();

            int activeAuthProviders = 0;

            var selectedGuids = new List<Guid>();
            GetAttributeValue( "RemoteAuthorizationTypes" ).SplitDelimitedValues()
                .ToList()
                .ForEach( v => selectedGuids.Add( v.AsGuid() ) );

            // Look for active external authentication providers
            foreach ( var serviceEntry in AuthenticationContainer.Instance.Components )
            {
                var component = serviceEntry.Value.Value;

                if ( component.IsActive &&
                    component.RequiresRemoteAuthentication &&
                    selectedGuids.Contains( component.EntityType.Guid ) )
                {
                    string loginTypeName = component.GetType().Name;

                    // Check if returning from third-party authentication
                    if ( !IsPostBack && component.IsReturningFromAuthentication( Request ) )
                    {
                        string userName = string.Empty;
                        string returnUrl = string.Empty;
                        string redirectUrlSetting = LinkedPageUrl( "RedirectPage" );
                        if ( component.Authenticate( Request, out userName, out returnUrl ) )
                        {
                            if ( !string.IsNullOrWhiteSpace( redirectUrlSetting ) )
                            {
                                LoginUser( userName, redirectUrlSetting, false );
                                PostAttendance( CurrentPerson );
                                AuthChurchOnline( CurrentPerson );
                                break;
                            }
                            else
                            {
                                LoginUser( userName, returnUrl, false );
                                PostAttendance( CurrentPerson );
                                AuthChurchOnline( CurrentPerson );
                                break;
                            }
                        }
                    }

                    activeAuthProviders++;

                    LinkButton lbLogin = new LinkButton();
                    phExternalLogins.Controls.Add( lbLogin );
                    lbLogin.AddCssClass( "btn btn-authenication " + loginTypeName.ToLower() );
                    lbLogin.ID = "lb" + loginTypeName + "Login";
                    lbLogin.Click += lbLogin_Click;
                    lbLogin.CausesValidation = false;

                    if ( !string.IsNullOrWhiteSpace( component.ImageUrl() ) )
                    {
                        HtmlImage img = new HtmlImage();
                        lbLogin.Controls.Add( img );
                        img.Attributes.Add( "style", "border:none" );
                        img.Src = Page.ResolveUrl( component.ImageUrl() );
                    }
                    else
                    {
                        lbLogin.Text = loginTypeName;
                    }
                }
            }

            // adjust the page if there are no social auth providers
            if ( activeAuthProviders == 0 )
            {
                divSocialLogin.Visible = false;
                divOrgLogin.RemoveCssClass( "col-sm-6" );
                divOrgLogin.AddCssClass( "col-sm-12" );
            }
        }

        /// <summary>
        /// Raises the <see cref="E:System.Web.UI.Control.Load" /> event.
        /// </summary>
        /// <param name="e">The <see cref="T:System.EventArgs" /> object that contains the event data.</param>
        protected override void OnLoad( EventArgs e )
        {
            base.OnLoad( e );

            if ( !Page.IsPostBack )
            {
                lPromptMessage.Text = GetAttributeValue( "PromptMessage" );
                tbUserName.Focus();
            }

            pnlMessage.Visible = false;
        }

        #endregion

        #region Events

        /// <summary>
        /// Handles the Click event of the btnLogin control.
        /// NOTE: This is the btnLogin for Internal Auth
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        protected void btnLogin_Click( object sender, EventArgs e )
        {
            if ( Page.IsValid )
            {
                var rockContext = new RockContext();
                var userLoginService = new UserLoginService( rockContext );
                var userLogin = userLoginService.GetByUserName( tbUserName.Text );
                if ( userLogin != null && userLogin.EntityType != null )
                {
                    var component = AuthenticationContainer.GetComponent( userLogin.EntityType.Name );
                    if ( component != null && component.IsActive && !component.RequiresRemoteAuthentication )
                    {
                        if ( component.Authenticate( userLogin, tbPassword.Text ) )
                        {
                            if ( ( userLogin.IsConfirmed ?? true ) && !( userLogin.IsLockedOut ?? false ) )
                            {
                                string returnUrl = Request.QueryString["returnurl"];
                                LoginUser( tbUserName.Text, returnUrl, cbRememberMe.Checked );
                                PostAttendance( userLogin.Person );
                                AuthChurchOnline( userLogin.Person );
                            }
                            else
                            {
                                var mergeFields = Rock.Lava.LavaHelper.GetCommonMergeFields( this.RockPage, this.CurrentPerson );

                                if ( userLogin.IsLockedOut ?? false )
                                {
                                    lLockedOutCaption.Text = GetAttributeValue( "LockedOutCaption" ).ResolveMergeFields( mergeFields );

                                    pnlLogin.Visible = false;
                                    pnlLockedOut.Visible = true;
                                }
                                else
                                {
                                    SendConfirmation( userLogin );

                                    lConfirmCaption.Text = GetAttributeValue( "ConfirmCaption" ).ResolveMergeFields( mergeFields );

                                    pnlLogin.Visible = false;
                                    pnlConfirmation.Visible = true;
                                }
                            }

                            return;
                        }
                    }
                }
            }

            string helpUrl = string.Empty;

            if ( !string.IsNullOrWhiteSpace( GetAttributeValue( "HelpPage" ) ) )
            {
                helpUrl = LinkedPageUrl( "HelpPage" );
            }
            else
            {
                helpUrl = ResolveRockUrl( "~/ForgotUserName" );
            }

            DisplayError( string.Format( "Sorry, we couldn't find an account matching that username/password. Can we help you <a href='{0}'>recover your account information</a>?", helpUrl ) );
        }



        /// <summary>
        /// Handles the Click event of the lbLogin control.
        /// NOTE: This is the lbLogin for External/Remote logins
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="EventArgs" /> instance containing the event data.</param>
        /// <exception cref="System.NotImplementedException"></exception>
        protected void lbLogin_Click( object sender, EventArgs e )
        {
            if ( sender is LinkButton )
            {
                LinkButton lb = ( LinkButton ) sender;

                foreach ( var serviceEntry in AuthenticationContainer.Instance.Components )
                {
                    var component = serviceEntry.Value.Value;
                    if ( component.IsActive && component.RequiresRemoteAuthentication )
                    {
                        string loginTypeName = component.GetType().Name;
                        if ( lb.ID == "lb" + loginTypeName + "Login" )
                        {
                            Uri uri = component.GenerateLoginUrl( Request );
                            if ( uri != null )
                            {
                                Response.Redirect( uri.AbsoluteUri, false );
                                Context.ApplicationInstance.CompleteRequest();
                                return;
                            }
                            else
                            {
                                DisplayError( string.Format( "ERROR: {0} does not have a remote login URL", loginTypeName ) );
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Handles the Click event of the btnLogin control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        protected void btnNewAccount_Click( object sender, EventArgs e )
        {
            string returnUrl = Request.QueryString["returnurl"];

            if ( !string.IsNullOrWhiteSpace( GetAttributeValue( "NewAccountPage" ) ) )
            {
                var parms = new Dictionary<string, string>();

                if ( !string.IsNullOrWhiteSpace( returnUrl ) )
                {
                    parms.Add( "returnurl", returnUrl );
                }

                NavigateToLinkedPage( "NewAccountPage", parms );
            }
            else
            {
                string url = "~/NewAccount";

                if ( !string.IsNullOrWhiteSpace( returnUrl ) )
                {
                    url += "?returnurl=" + returnUrl;
                }

                Response.Redirect( url, false );
                Context.ApplicationInstance.CompleteRequest();
            }
        }

        /// <summary>
        /// Handles the Click event of the btnHelp control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        protected void btnHelp_Click( object sender, EventArgs e )
        {
            if ( !string.IsNullOrWhiteSpace( GetAttributeValue( "HelpPage" ) ) )
            {
                NavigateToLinkedPage( "HelpPage" );
            }
            else
            {
                Response.Redirect( "~/ForgotUserName", false );
                Context.ApplicationInstance.CompleteRequest();
            }
        }

        #endregion

        #region Methods

        /// <summary>
        /// Displays the error.
        /// </summary>
        /// <param name="message">The message.</param>
        private void DisplayError( string message )
        {
            pnlMessage.Controls.Clear();
            pnlMessage.Controls.Add( new LiteralControl( message ) );
            pnlMessage.Visible = true;
        }

        /// <summary>
        /// Logs in the user.
        /// </summary>
        /// <param name="userName">Name of the user.</param>
        /// <param name="returnUrl">The return URL.</param>
        /// <param name="rememberMe">if set to <c>true</c> [remember me].</param>
        private void LoginUser( string userName, string returnUrl, bool rememberMe )
        {
            string redirectUrlSetting = LinkedPageUrl( "RedirectPage" );

            UserLoginService.UpdateLastLogin( userName );

            Rock.Security.Authorization.SetAuthCookie( userName, rememberMe, false );
            if ( !string.IsNullOrWhiteSpace( returnUrl ) )
            {
                string redirectUrl = Server.UrlDecode( returnUrl );
                Response.Redirect( redirectUrl );
                Context.ApplicationInstance.CompleteRequest();
            }
        }

        /// <summary>
        /// Sends the confirmation.
        /// </summary>
        /// <param name="userLogin">The user login.</param>
        private void SendConfirmation( UserLogin userLogin )
        {
            string url = LinkedPageUrl( "ConfirmationPage" );
            if ( string.IsNullOrWhiteSpace( url ) )
            {
                url = ResolveRockUrl( "~/ConfirmAccount" );
            }

            var mergeFields = Rock.Lava.LavaHelper.GetCommonMergeFields( this.RockPage, this.CurrentPerson );
            mergeFields.Add( "ConfirmAccountUrl", RootPath + url.TrimStart( new char[] { '/' } ) );

            var personDictionary = userLogin.Person.ToLiquid() as Dictionary<string, object>;
            mergeFields.Add( "Person", personDictionary );
            mergeFields.Add( "User", userLogin );

            var recipients = new List<RecipientData>();
            recipients.Add( new RecipientData( userLogin.Person.Email, mergeFields ) );

            Email.Send( GetAttributeValue( "ConfirmAccountTemplate" ).AsGuid(), recipients, ResolveRockUrl( "~/" ), ResolveRockUrl( "~~/" ), false );
        }

        public void PostAttendance( Person person )
        {
            var checkinGroupValue = GetAttributeValue( "CheckInGroup" );
            var checkinLocationValue = GetAttributeValue( "OnlineCampusLocation" );
            var checkinScheduleValues = GetAttributeValue( "OnlineCampusSchedules" );

            if ( !string.IsNullOrWhiteSpace( checkinGroupValue ) && !string.IsNullOrWhiteSpace( checkinLocationValue ) && !string.IsNullOrWhiteSpace( checkinScheduleValues ) )
            {
                var rockContext = new RockContext();

                var group = new GroupService( rockContext ).Get( checkinGroupValue.Split( '|' )[1].AsGuid() );
                var campus = new CampusService( rockContext ).Get( checkinLocationValue.AsGuid() );

                var scheduleGuids = checkinScheduleValues.Split( ',' ).AsGuidList();
                var schedule = new ScheduleService( rockContext )
                    .GetByGuids( scheduleGuids )
                    .ToList()
                    .Where( s => s.IsCheckInActive && s.IsCheckInEnabled )
                    .FirstOrDefault();

                if ( group != null && campus != null && schedule != null )
                {
                    var attendanceService = new AttendanceService( rockContext );
                    var attendance = new Attendance();
                    attendance.PersonAliasId = person.PrimaryAliasId ?? person.PrimaryAlias.Id;
                    attendance.CampusId = campus.Id;
                    attendance.ScheduleId = schedule.Id;
                    attendance.GroupId = group.Id;
                    attendance.StartDateTime = RockDateTime.Now;
                    attendance.DidAttend = true;
                    attendance.EndDateTime = null;
                    attendanceService.Add( attendance );
                    rockContext.SaveChanges();
                }


            }
        }

        private MultiPass BuildMultipass( Person person )
        {
            var multiPass = new MultiPass();
            multiPass.Email = person.Email;
            multiPass.FirstName = person.FirstName;
            multiPass.LastName = person.LastName;
            multiPass.Nickname = !string.IsNullOrWhiteSpace( person.NickName ) ? person.NickName : person.FirstName;
            multiPass.Expires = RockDateTime.Now.AddMinutes( 5 ).ToUniversalTime().ToString( "yyyy-MM-ddTHH:mm:ssZ" );
            return multiPass;
        }
        private void AuthChurchOnline( Person person )
        {
            var ssoKey = GetAttributeValue( "SSOKey" );
            var multipass = BuildMultipass( person );
            var json = JsonConvert.SerializeObject( multipass );
            if ( !string.IsNullOrWhiteSpace( json ) && !string.IsNullOrWhiteSpace( ssoKey ) )
            {
                // Sha256 hash of the SSO key                
                var sha256 = SHA256.Create();
                byte[] keyByte = Encoding.UTF8.GetBytes( ssoKey );
                byte[] hashKey = sha256.ComputeHash( keyByte );
                // TODO Rotate
                string initVector = "OpenSSL for Ruby"; 

                byte[] initVectorBytes = Encoding.UTF8.GetBytes( initVector );
                byte[] toEncrypt = Encoding.UTF8.GetBytes( initVector + json );
                byte[] encryptedData = encryptStringToBytes_AES( toEncrypt, hashKey, initVectorBytes );

                // Convert plain text to bytes
                var cipherTextWithSpaces = Convert.ToBase64String( encryptedData )
                    .ToCharArray()
                    .Where( c => !Char.IsWhiteSpace( c ) ).ToArray();
                string cipherText = new string( cipherTextWithSpaces );

                string sha1Hash;
                using ( var hmacsha1 = new HMACSHA1( keyByte ) )
                {
                    byte[] hashmessage = hmacsha1.ComputeHash( Encoding.UTF8.GetBytes( cipherText) );
                    sha1Hash = Convert.ToBase64String( hashmessage );
                }

                string urlRedirect = GetAttributeValue( "RedirectURL" );
                Response.Redirect( ( urlRedirect + "/sso?sso=" + cipherText.UrlEncode() + "&signature=" + sha1Hash.UrlEncode() ), false );
                Context.ApplicationInstance.CompleteRequest();
                return;
            }
            throw new Exception( "Error building authentication code for Church Online" );
        }

        static byte[] encryptStringToBytes_AES( byte[] textBytes, byte[] Key, byte[] IV )
        {
            // Declare the stream used to encrypt to an in memory
            // array of bytes and the RijndaelManaged object
            // used to encrypt the data.
            using ( MemoryStream msEncrypt = new MemoryStream() )
            using ( RijndaelManaged aesAlg = new RijndaelManaged() )
            {
                // Provide the RijndaelManaged object with the specified key and IV.
                aesAlg.Mode = CipherMode.CBC;
                aesAlg.Padding = PaddingMode.PKCS7;
                aesAlg.KeySize = 256;
                aesAlg.BlockSize = 128;
                aesAlg.Key = Key;
                aesAlg.IV = IV;
                // Create an encrytor to perform the stream transform.
                ICryptoTransform encryptor = aesAlg.CreateEncryptor();

                // Create the streams used for encryption.
                using ( CryptoStream csEncrypt = new CryptoStream( msEncrypt, encryptor, CryptoStreamMode.Write ) )
                {
                    csEncrypt.Write( textBytes, 0, textBytes.Length );
                    csEncrypt.FlushFinalBlock();
                }

                byte[] encrypted = msEncrypt.ToArray();
                // Return the encrypted bytes from the memory stream.
                return encrypted;
            }
        }

        #endregion

        internal class MultiPass
        {
            [JsonProperty( PropertyName = "email" )]
            public string Email { get; set; }
            [JsonProperty( PropertyName = "expires" )]
            public string Expires { get; set; }
            [JsonProperty( PropertyName = "first_name" )]
            public string FirstName { get; set; }
            [JsonProperty( PropertyName = "last_name" )]
            public string LastName { get; set; }
            [JsonProperty( PropertyName = "nickname" )]
            public string Nickname { get; set; }
        }
        
    }
}
