import styles from "./Footer.module.css";
import LinkTo from "../../Link/Link";
import Tooltip from "../../Tooltip/Tooltip";
export default function Footer() {
    return (
        <footer className={styles.footer}>

            <div className={styles.footerContent}>

                <div className={styles.column}>
                    <h4>phyloDIVaS</h4>
                    <p>Empowering evolutionary biology with accessible genomic analysis tools.</p>
                    <p className={styles.contact}>Contact: shrihith.golugula@temple.edu</p>
                </div>

                <div className={styles.column}>
                    <h4>Explore</h4>
                    <div>
                    <Tooltip text="Search for genes, species, and assemblies">
                        <LinkTo label="Search" linkTo="/Search" />
                        </Tooltip>
                    </div>

                    <div>
                    <Tooltip text="View your previous searches and analyses">
                        <LinkTo label="History" linkTo="/GrantInfo" />
                        </Tooltip>
                    </div>
                    <div>
                    <Tooltip text="Learn more about our project and team">
                        <LinkTo label="About Us" linkTo="/About" />
                        </Tooltip>
                    </div>
                    <div>
                    <Tooltip text="Send us feedback or future suggestions">
                        <LinkTo label="Feedback" linkTo="/Feedback" />
                        </Tooltip>
                    </div>
                </div>

                <div className={styles.column}>
                    <h4>Account</h4>
                    <div>
                    <Tooltip text="Sign in to access saved searches">
                        <LinkTo label="Login" linkTo="/Login" />
                    </Tooltip>
                    </div>
                    <div>
                    <Tooltip text="Create a new account">
                        <LinkTo label="Register" linkTo="/Register" />
                        </Tooltip>
                    </div>
                    <div>
                    <Tooltip text="Get help using phyloDIVaS">
                        <LinkTo label="Help" linkTo="/Help" />
                        </Tooltip>
                    </div>

                    <div>
                    <Tooltip text="Read our disclaimer and data policy">
                        <LinkTo label="Disclaimer" linkTo="/Disclaimer" />
                        </Tooltip>
                    </div>

                </div>
            </div>

            <div className={styles.footerBottom}>
                <p>&copy; {new Date().getFullYear()} Team phyloDIVaS. All rights reserved.</p>
            </div>
        </footer>
    );
}
